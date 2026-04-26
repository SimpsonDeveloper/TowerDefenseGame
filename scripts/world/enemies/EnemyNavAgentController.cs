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
///   1. Pick the closest candidate tower in TargetGroup.
///   2. Pre-pick a path destination on the footprint's outward-facing edge
///      nearest the enemy (geometric, no nav query).
///   3. Run a single NavigationServer2D.MapGetPath to that destination.
///   4. Walk the returned path corners; the first corner within
///      (agentRadius + AttackRange) of the footprint is the stop zone.
///   5. Refine via bisection on the entering segment so the final point sits
///      precisely on the standoff boundary, oriented along the approach.
///   6. If no corner enters the standoff zone, retry with the next candidate.
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
    /// Picks a tower, computes a reachable approach point, and feeds it to the
    /// nav agent. Retries with the next tower if no approach point is reachable.
    /// </summary>
    private void RetargetAndSetDestination()
    {
        if (string.IsNullOrEmpty(TargetGroup)) { _target = null; return; }

        Viewport viewport = GetViewport();
        List<Node2D> candidates = new();
        foreach (Node node in GetTree().GetNodesInGroup(TargetGroup))
        {
            if (node is not Node2D n2d || n2d.GetViewport() != viewport) continue;
            candidates.Add(n2d);
        }
        candidates.Sort((a, b) =>
            GlobalPosition.DistanceSquaredTo(a.GlobalPosition)
                .CompareTo(GlobalPosition.DistanceSquaredTo(b.GlobalPosition)));

        foreach (Node2D tower in candidates)
        {
            if (TryResolveApproachPoint(tower, out Vector2 approach))
            {
                _target = tower;
                NavAgent.TargetPosition = approach;
                return;
            }
        }

        _target = null;
    }

    /// <summary>
    /// Resolves an approach point for <paramref name="tower"/>:
    ///   1. Footprint picks a navmesh-reachable destination near an outward
    ///      edge — iterates tiles by distance to enemy, snaps each candidate
    ///      edge point to the navmesh, accepts first whose snap-to-edge
    ///      distance ≤ max(agentRadius, AttackRange). Rejects unreachable
    ///      edges (deep in obstacle / disconnected nav island).
    ///   2. MapGetPath to that destination.
    ///   3. Walk path corners; first corner whose distance to the footprint
    ///      is ≤ max(agentRadius, AttackRange) is the stop zone.
    ///   4. Refine with bisection along the entering segment to land
    ///      precisely on the standoff boundary.
    /// </summary>
    private bool TryResolveApproachPoint(Node2D tower, out Vector2 approach)
    {
        approach = default;
        Rid navMap = GetWorld2D().NavigationMap;
        if (!navMap.IsValid) return false;

        var tracker = TowerFootprintTracker.Instance;
        if (tracker == null || !tracker.TryGetFootprint(tower, out var footprint)) return false;

        float agentRadius = EnemyConfig?.AgentRadius ?? 0f;
        float standoff = Mathf.Max(agentRadius, AttackRange);
        float standoffSq = standoff * standoff;

        if (!footprint.TryFindApproachDestination(GlobalPosition, standoff, navMap, out Vector2 destination))
            return false;

        Vector2[] path = NavigationServer2D.MapGetPath(
            navMap, GlobalPosition, destination, true);
        if (path.Length == 0) return false;

        for (int i = 0; i < path.Length; i++)
        {
            if (footprint.DistanceSqTo(path[i]) <= standoffSq)
            {
                approach = i == 0
                    ? path[0]
                    : footprint.FindStandoffPoint(path[i - 1], path[i], standoffSq);
                return true;
            }
        }
        return false;
    }
}
