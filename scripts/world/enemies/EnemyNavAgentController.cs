using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Enemy variant that delegates pathfinding to Godot's built-in NavigationAgent2D.
///
/// Targeting strategy (vs. plain "closest tower center"):
///   1. Pick the closest candidate tower in TargetGroup.
///   2. Raycast from the enemy toward the tower's center. If the ray hits the
///      target tower's body, snap the hit point to the nav mesh and use it as
///      the path destination — this gives the enemy a natural enemy-side
///      approach rather than the euclidean-closest nav polygon to the center,
///      which often causes wrap-around.
///   3. If the raycast fails or its snapped point is unreachable, query a
///      single path toward the tower's center. The tower counts as reachable
///      when the path's endpoint tile lies within one agent-radius (in tile
///      steps) of the footprint, in which case the endpoint is used as the
///      approach. Otherwise, retry with the next closest candidate tower.
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
    [Export] public float TargetDesiredDistance { get; set; } = 8f;

    /// <summary>
    /// Seconds between retargets. Each retarget runs a raycast and potentially
    /// several MapGetPath reachability queries, so keep this above 0.1s.
    /// </summary>
    [Export] public float TargetUpdateInterval { get; set; } = 0.25f;

    /// <summary>
    /// Tolerance (px²) used when deciding whether MapGetPath's endpoint matches
    /// the requested candidate — i.e. whether the path actually reaches it.
    /// </summary>
    [Export] public float ReachableEndpointTolerance { get; set; } = 4f;

    [ExportGroup("Target")]
    [Export] public string TargetGroup { get; set; } = "Player";

    /// <summary>
    /// Collision mask used when raycasting toward a tower center. Should
    /// include the tower body collision layer (default 16 = layer 5).
    /// </summary>
    [Export(PropertyHint.Layers2DPhysics)] public uint TowerRaycastMask { get; set; } = 16;

    /// <summary>
    /// Reach of the enemy's attack. Snapped approach points are nudged
    /// outward by this amount along the direction from tower → point, so the
    /// enemy stops just inside its own reach rather than walking all the way
    /// to the surface. Set to 0 for strict melee-on-contact.
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
            Velocity = Velocity.Lerp(Vector2.Zero, Acceleration * (float)delta);
            MoveAndSlide();
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

    private bool TryResolveApproachPoint(Node2D tower, out Vector2 approach)
    {
        approach = default;
        Rid navMap = GetWorld2D().NavigationMap;
        if (!navMap.IsValid) return false;

        // Step 1: raycast toward tower center. The hit gives an enemy-side
        // surface point, which avoids wrap-around when the nav-mesh-closest
        // point to tower center lies on the far side of the tower.
        if (TryRaycastApproach(tower, navMap, out Vector2 rayPoint))
        {
            approach = rayPoint;
            return true;
        }

        // Step 2: single path query toward tower center. Tower is reachable
        // if the path's endpoint tile lies within one agent-radius of the
        // tower footprint (in tile steps). Avoids per-perimeter-point path
        // queries.
        var tracker = TowerFootprintTracker.Instance;
        if (tracker == null || tracker.Coords == null) return false;

        Vector2[] path = NavigationServer2D.MapGetPath(
            navMap, GlobalPosition, tower.GlobalPosition, true);
        if (path.Length == 0) return false;
        Vector2 endpoint = path[^1];

        float agentRadius = EnemyConfig?.AgentRadius ?? 0f;
        int maxTiles = Mathf.CeilToInt(agentRadius / tracker.Coords.TilePixelSize);

        if (tracker.IsWithinTileReach(tower, endpoint, maxTiles))
        {
            approach = NudgeForAttackRange(endpoint, tower.GlobalPosition, navMap);
            return true;
        }
        return false;
    }

    private bool TryRaycastApproach(Node2D tower, Rid navMap, out Vector2 snapped)
    {
        snapped = default;
        var space = GetWorld2D().DirectSpaceState;
        var query = PhysicsRayQueryParameters2D.Create(GlobalPosition, tower.GlobalPosition);
        query.CollisionMask = TowerRaycastMask;
        query.CollideWithAreas = false;
        query.Exclude = [GetRid()];

        var hit = space.IntersectRay(query);
        if (hit.Count == 0) return false;
        // Reject hits on other bodies (e.g. another tower between us and target).
        if (hit["collider"].AsGodotObject() != tower) return false;

        Vector2 hitPos = (Vector2)hit["position"];
        Vector2 rawSnap = NavigationServer2D.MapGetClosestPoint(navMap, hitPos);
        // Reject snaps that moved far — likely landed across a wall.
        float agentRadius = EnemyConfig?.AgentRadius ?? 0f;
        float maxSnapJump = Math.Max(agentRadius, AttackRange);
        if (rawSnap.DistanceSquaredTo(hitPos) > 2 * maxSnapJump * maxSnapJump + 0.1f) return false;

        snapped = NudgeForAttackRange(rawSnap, tower.GlobalPosition, navMap);
        return true;
    }

    /// <summary>
    /// Pushes <paramref name="point"/> outward from <paramref name="towerCenter"/>
    /// by AttackRange and re-snaps to the nav mesh. Ensures the enemy stops
    /// just inside its attack reach rather than walking all the way to the
    /// tower's surface. No-op when AttackRange is 0.
    /// </summary>
    private Vector2 NudgeForAttackRange(Vector2 point, Vector2 towerCenter, Rid navMap)
    {
        if (AttackRange <= 0f) return point;
        Vector2 outward = (point - towerCenter);
        if (outward.LengthSquared() < 1e-6f) return point;
        Vector2 nudged = point + outward.Normalized() * AttackRange;
        return NavigationServer2D.MapGetClosestPoint(navMap, nudged);
    }
}
