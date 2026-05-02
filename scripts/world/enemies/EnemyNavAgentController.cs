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
///   1. Sort candidates in TargetGroup by Euclidean distance to the enemy.
///   2. For the next candidate tower, run two MapGetPath queries in parallel:
///        A. Destination = Euclidean-closest reachable footprint edge
///           (validated via per-edge MapGetClosestPoint snap).
///        B. Destination = tower node position; MapGetPath snaps it to the
///           navmesh, so the path enters the standoff zone via the
///           path-shortest side (handles winding-maze geometry where A's
///           Euclidean pick would force a long detour).
///   3. Score each path: walk corners until one is within
///      (max(agentRadius, AttackRange)) of the footprint perimeter, then
///      refine via bisection on the entering segment so the final point sits
///      precisely on the standoff boundary. Score = polyline length up to
///      that refined point.
///   4. Pick the shorter-scored path; that's the approach point.
///   5. If neither path enters the standoff zone, retry with the next candidate.
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

        RetargetAndSetDestination();
        OnReady();
    }

    public sealed override void _PhysicsProcess(double delta)
    {
        QueueRedraw();
        _targetUpdateTimer -= (float)delta;
        bool targetGone = _target == null || !IsInstanceValid(_target);
        if (_targetUpdateTimer <= 0f || targetGone)
        {
            _targetUpdateTimer = TargetUpdateInterval;
            RetargetAndSetDestination();
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
    /// Snapshots candidate towers (Euclidean-sorted), invokes the pure
    /// <see cref="EnemyApproachResolver"/>, and applies the result. The
    /// snapshot/resolve split is deliberate: only the snapshot step touches
    /// scene-graph state, so the resolve step can later be moved to a worker
    /// thread without changing what's captured here.
    /// </summary>
    private void RetargetAndSetDestination()
    {
        if (string.IsNullOrEmpty(TargetGroup)) { _target = null; return; }

        Rid navMap = GetWorld2D().NavigationMap;
        var tracker = TowerFootprintTracker.Instance;
        if (tracker == null) { _target = null; return; }

        Viewport viewport = GetViewport();
        List<ApproachCandidate> candidates = new();
        Dictionary<ulong, Node2D> towersById = new();
        foreach (Node node in GetTree().GetNodesInGroup(TargetGroup))
        {
            if (node is not Node2D n2d || n2d.GetViewport() != viewport) continue;
            if (!tracker.TryGetFootprint(n2d, out var footprint)) continue;
            ulong id = n2d.GetInstanceId();
            candidates.Add(new ApproachCandidate(id, n2d.GlobalPosition, footprint));
            towersById[id] = n2d;
        }
        Vector2 enemyPos = GlobalPosition;
        candidates.Sort((a, b) =>
            enemyPos.DistanceSquaredTo(a.TowerPosition)
                .CompareTo(enemyPos.DistanceSquaredTo(b.TowerPosition)));

        float standoff = Mathf.Max(EnemyConfig?.AgentRadius ?? 0f, AttackRange);
        ApproachResult result = EnemyApproachResolver.Resolve(enemyPos, navMap, standoff, candidates);

        if (result.Found && towersById.TryGetValue(result.TowerInstanceId, out Node2D tower))
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
