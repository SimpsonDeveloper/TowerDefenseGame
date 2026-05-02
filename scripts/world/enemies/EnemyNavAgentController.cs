using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Enemy variant that delegates pathfinding to Godot's built-in NavigationAgent2D.
///
/// Targeting strategy (per retarget):
///   1. Snapshot scene-graph state on the main thread: enemy position,
///      candidate towers (Euclidean-sorted) + footprint refs, nav map RID.
///   2. Submit the snapshot to <see cref="EnemyPathfindService"/>, which runs
///      <see cref="EnemyApproachResolver"/> on a worker thread.
///      The resolver iterates candidates and, for each, runs two MapGetPath
///      queries:
///        A. Destination = Euclidean-closest reachable footprint edge
///           (validated via per-edge MapGetClosestPoint snap).
///        B. Destination = tower node position; MapGetPath snaps it to the
///           navmesh, so the path enters the standoff zone via the
///           path-shortest side (handles winding-maze geometry where A's
///           Euclidean pick would force a long detour).
///      Each path is scored: walk corners until one is within
///      (max(agentRadius, AttackRange)) of the footprint perimeter, then
///      refine via bisection on the entering segment. Score = polyline
///      length up to that refined point. Shorter wins.
///   3. On a later physics tick the controller drains the result, validates
///      the chosen tower is still alive, and assigns NavAgent.TargetPosition.
///      Until the result arrives, the enemy keeps walking toward its prior
///      target (or holds still if it has none).
///
/// Note: reachability is tested via NavigationServer2D.MapGetPath; NavAgent
/// then re-queries internally when TargetPosition is set. Accepted duplicate
/// in exchange for NavAgent doing path-follow and smoothing for us.
/// </summary>
[GlobalClass]
public partial class EnemyNavAgentController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 10f;

    [ExportGroup("Navigation")]
    [Export] public NavigationAgent2D NavAgent;
    [Export] public CollisionShape2D Hitbox { get; set; }
    [Export] public EnemyConfig EnemyConfig { get; set; }

    /// <summary>
    /// How close the agent must get to each path waypoint before it advances.
    /// Keep this at roughly half a tile width (4px for 8px tiles).
    /// </summary>
    [Export] public float PathDesiredDistance { get; set; } = 8f;

    /// <summary>
    /// How close the agent must get to the final target before navigation is
    /// considered finished.
    /// </summary>
    [Export] public float TargetDesiredDistance { get; set; } = 1f;

    /// <summary>
    /// Seconds between retargets. Each retarget runs two MapGetPath (one for reachability and one for agent) queries, so keep this above 0.1s.
    /// </summary>
    [Export] public float TargetUpdateInterval { get; set; } = 0.25f;

    /// <summary>
    /// Maximum age, in milliseconds, of a path-resolve result before it's
    /// discarded. If the worker is backlogged and the result lands too late,
    /// the enemy state has moved enough that the approach point is no longer
    /// valid; better to drop it and let the next retarget cycle resubmit.
    /// </summary>
    [Export] public int MaxResultAgeMs { get; set; } = 500;

    [ExportGroup("Target")]
    [Export] public string TargetGroup { get; set; } = "Towers";

    /// <summary>
    /// Reach of the enemy's attack. Added to agentRadius to form the standoff
    /// distance from the tower footprint at which the approach path is cut
    /// short. Set to 0 for strict melee-on-contact.
    /// </summary>
    [Export] public float AttackRange { get; set; } = 0f;

    [ExportGroup("Sprite")]
    [Export] SpriteComponent Sprite { get; set; }

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    /// <param name="delta">Physics delta in seconds.</param>
    /// <param name="distanceToTarget">Straight-line distance to target, or float.MaxValue.</param>
    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ────────────────────────────────────────────────────
    private Node2D _target;
    private float _targetUpdateTimer;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public sealed override void _Ready()
    {
        NavAgent.PathDesiredDistance = PathDesiredDistance;
        NavAgent.TargetDesiredDistance = TargetDesiredDistance;
        NavAgent.MaxSpeed = MoveSpeed;
        if (EnemyConfig != null)
        {
            NavAgent.Radius = EnemyConfig.AgentRadius;
            if (Hitbox?.Shape is CircleShape2D circle)
                circle.Radius = EnemyConfig.AgentRadius;
            else
                GD.PushWarning($"{Name}: Hitbox missing or not a CircleShape2D — physical radius won't match nav radius.");
        }
        else
        {
            GD.PushWarning($"{Name}: EnemyConfig not assigned — NavAgent.Radius and hitbox may not match nav-bake radius.");
        }

        AddToGroup("enemies");

        SubmitRetarget();
        OnReady();
    }

    public sealed override void _ExitTree()
    {
        // Block on any in-flight resolve so the worker isn't reading our
        // candidate snapshot after we're freed.
        EnemyPathfindService.Instance?.Cancel(GetInstanceId());
    }

    public sealed override void _PhysicsProcess(double delta)
    {
        QueueRedraw();

        DrainPendingResult();

        _targetUpdateTimer -= (float)delta;
        bool targetGone = _target != null && !IsInstanceValid(_target);
        if (targetGone) _target = null;
        if (_targetUpdateTimer <= 0f || _target == null)
        {
            _targetUpdateTimer = TargetUpdateInterval;
            SubmitRetarget();
        }

        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        if (_target == null || NavAgent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            OnPhysicsTick(delta, distToTarget);
            return;
        }

        Vector2 nextPos = NavAgent.GetNextPathPosition();
        Vector2 desiredVelocity = (nextPos - GlobalPosition).Normalized() * MoveSpeed;
        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        if (Sprite != null)
        {
            if (Velocity.X > 0)      Sprite.FlipH = true;
            else if (Velocity.X < 0) Sprite.FlipH = false;
        }

        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public Node2D Target => _target;

    public void ClearTarget()
    {
        _target = null;
        Velocity = Vector2.Zero;
    }

    // ── Targeting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshots scene-graph state (enemy position, candidate towers, footprint
    /// refs, nav map RID) and submits an off-thread resolve via
    /// <see cref="EnemyPathfindService"/>. No-op if a prior submission is still
    /// in-flight or completed-but-undrained — the existing job's result will
    /// arrive via <see cref="DrainPendingResult"/> first.
    /// </summary>
    private void SubmitRetarget()
    {
        var service = EnemyPathfindService.Instance;
        if (service == null || string.IsNullOrEmpty(TargetGroup)) return;
        if (service.HasJob(GetInstanceId())) return;

        Rid navMap = GetWorld2D().NavigationMap;
        var tracker = TowerFootprintTracker.Instance;
        if (tracker == null) return;

        Viewport viewport = GetViewport();
        List<ApproachCandidate> candidates = new();
        foreach (Node node in GetTree().GetNodesInGroup(TargetGroup))
        {
            if (node is not Node2D n2d || n2d.GetViewport() != viewport) continue;
            if (!tracker.TryGetFootprint(n2d, out var footprint)) continue;
            candidates.Add(new ApproachCandidate(n2d.GetInstanceId(), n2d.GlobalPosition, footprint));
        }
        Vector2 enemyPos = GlobalPosition;
        candidates.Sort((a, b) =>
            enemyPos.DistanceSquaredTo(a.TowerPosition)
                .CompareTo(enemyPos.DistanceSquaredTo(b.TowerPosition)));

        float standoff = Mathf.Max(EnemyConfig?.AgentRadius ?? 0f, AttackRange);
        service.Submit(GetInstanceId(), enemyPos, navMap, standoff, candidates);
    }

    /// <summary>
    /// Polls the pathfind service for our most recent submission. If a result
    /// is ready, validates the chosen tower is still alive (it may have been
    /// destroyed while the resolve ran) and applies it as the new target.
    /// </summary>
    private void DrainPendingResult()
    {
        var service = EnemyPathfindService.Instance;
        if (service == null) return;
        if (!service.TryConsume(GetInstanceId(), out ApproachResult result, out ulong ageMs)) return;
        if (ageMs > (ulong)MaxResultAgeMs) return; // stale; next retarget will resubmit

        if (!result.Found)
        {
            _target = null;
            return;
        }

        GodotObject obj = GodotObject.InstanceFromId(result.TowerInstanceId);
        if (obj is Node2D tower && IsInstanceValid(tower))
        {
            _target = tower;
            NavAgent.TargetPosition = result.Approach;
        }
        else
        {
            _target = null;
        }
    }
}
