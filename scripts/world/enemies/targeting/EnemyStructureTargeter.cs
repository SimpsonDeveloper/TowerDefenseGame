using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;
using towerdefensegame.scripts.world.paths;
using towerdefensegame.scripts.world.paths.core;

namespace towerdefensegame.scripts.world.enemies.targeting;

/// <summary>
/// Selects which <b>structure</b> the enemy walks toward and emits the navmesh-validated
/// approach point for an <see cref="EnemyNavController"/> (or any consumer) to
/// follow. Which structures are in play is <see cref="TargetGroup"/>: <c>Towers</c> to hunt
/// turrets, <c>Core</c> to head for the core and let the attacker deal with whatever is in the
/// way. The strategy below is identical either way, which is why there is one class.
///
/// Targeting strategy (per retarget):
///   1. Snapshot scene-graph state on the main thread: enemy position,
///      candidate towers (Euclidean-sorted) + footprint refs, nav map RID.
///   2. Submit the snapshot to <see cref="EnemyPathfindService"/>, which runs
///      <see cref="EnemyApproachResolver"/> on a worker thread.
///   3. On a later <see cref="Tick"/>, drain the result, validate the chosen
///      tower is still alive, and raise <see cref="EnemyTargeter.ApproachResolved"/>
///      with the destination — or <see cref="EnemyTargeter.TargetCleared"/> if no
///      tower is reachable.
///
/// The targeter never touches movement state; the mover subscribes and decides
/// what to do with the destination.
///
/// <b>Following a route.</b> With <see cref="FollowsRoute"/> set and wards laid in this viewport,
/// the targeter stops choosing structures and walks the <see cref="MiragePath"/> instead: joined at
/// the far end, ward by ward inward, then the last leg into the core. Each ward is emitted as a
/// plain destination; no resolver job runs while one is being walked. Which ward, and what happens
/// when the player edits the route mid-walk, is <see cref="RouteCursor{T}"/>'s business, not this
/// class's. Design: <c>impl-planning/combat/paths.md</c> §8.
/// </summary>
[GlobalClass]
public partial class EnemyStructureTargeter : EnemyTargeter
{
    /// <summary>
    /// Which group of structures to choose a target from. Set by the
    /// <see cref="TargetingPattern"/> that built this. Every candidate must carry a footprint in
    /// the <see cref="TowerFootprintTracker"/>, which is what the approach resolver stands a
    /// target off from.
    /// </summary>
    public string TargetGroup { get; set; } = "Towers";

    /// <summary>
    /// Structures the enemy will break off to attack when one comes within
    /// <see cref="AggroRadius"/>. Empty means it never breaks off and walks past everything.
    /// </summary>
    public string AggroGroup { get; set; }

    /// <summary>Pixels. Zero disables breaking off entirely.</summary>
    public float AggroRadius { get; set; }

    /// <summary>
    /// Walk the player's route when one is laid. Set by <see cref="CoreTargetingPattern"/>; a
    /// turret hunter has no business on a route to the core.
    /// </summary>
    public bool FollowsRoute { get; set; }

    /// <summary>
    /// While true the enemy cannot break off, whatever it walks past — it is following a route it
    /// believes in, wards and the final leg home alike. Derived from the route cursor rather than
    /// stored, so there is one answer. A <b>deviating</b> enemy will step off the route and become
    /// free to turn on a turret beside it (<c>impl-planning/combat/paths.md</c>); deviation is not
    /// built, so for now only an empty route frees an enemy.
    /// </summary>
    public bool Railroaded => _cursor.OnRoute;

    /// <summary>The ward being walked toward, in world space, or null when not on a ward leg. Debug readout.</summary>
    public Vector2? Waypoint { get; private set; }

    /// <summary>
    /// Pixels from a ward's centre that count as reaching it. A tile: tighter than that and a
    /// crowd jostling round one ward can hold an enemy off it forever.
    /// </summary>
    public float WardArriveRadius { get; set; } = 16f;

    /// <summary>
    /// Reach of the enemy's attack. Added to agentRadius to form the standoff
    /// distance from the tower footprint at which the approach path is cut
    /// short. Set to 0 for strict melee-on-contact.
    /// </summary>
    public float AttackRange { get; set; }

    private Node2D _owner;
    private PathfindingResourceCoordinator _coordinator;
    private TowerFootprintTracker _footprints;
    private float _retargetTimer;

    private MiragePath _route;
    private readonly RouteCursor<Vector2I> _cursor = new();

    /// <summary>
    /// A ward chosen but not yet announced. Emitted from <see cref="Tick"/> rather than on the
    /// spot, because the first ward is chosen in <c>_Ready</c> — and a child readies before its
    /// parent, so the controller has not subscribed yet and an immediate emit would be lost.
    /// </summary>
    private bool _announceWaypoint;

    public override void _Ready()
    {
        _owner = GetParent<Node2D>();
        Viewport viewport = GetViewport();
        _coordinator = PathfindingResourceCoordinator.ForViewport(viewport);
        _footprints  = TowerFootprintTracker.ForViewport(viewport);
        if (_coordinator != null)
            _coordinator.ResourcesReady += OnResourcesReady;

        if (FollowsRoute)
        {
            _route = MiragePath.ForViewport(viewport);
            if (_route != null)
            {
                _route.Changed += OnRouteChanged;
                _cursor.Sync(_route.Wards);
            }
        }

        if (_cursor.Stage == RouteStage.Following) HeadForWard();
        else TryRetarget();
    }

    public override void _ExitTree()
    {
        if (_coordinator != null)
            _coordinator.ResourcesReady -= OnResourcesReady;

        if (_route != null)
            _route.Changed -= OnRouteChanged;

        if (_owner != null)
            EnemyPathfindService.Instance?.Cancel(_owner.GetInstanceId());
    }

    /// <summary>
    /// Driven by the owning controller in physics order so drain happens before
    /// movement consumes the destination on the same tick.
    /// </summary>
    public override void Tick(double delta)
    {
        if (_cursor.Stage == RouteStage.Following)
        {
            if (_announceWaypoint && Waypoint.HasValue)
            {
                _announceWaypoint = false;
                EmitApproachResolved(Waypoint.Value);
            }

            if (Waypoint.HasValue && _owner != null
                && _owner.GlobalPosition.DistanceTo(Waypoint.Value) <= WardArriveRadius)
            {
                _cursor.Advance();
                OnStageMoved();
            }
            return;
        }

        DrainPendingPathResult();

        _retargetTimer -= (float)delta;
        if (CurrentTarget != null && !IsInstanceValid(CurrentTarget))
            ClearTarget();
        if (_retargetTimer <= 0f || CurrentTarget == null)
            TryRetarget();
    }

    /// <summary>Drops the current target and notifies subscribers.</summary>
    public void ClearTarget()
    {
        if (CurrentTarget == null) return;
        CurrentTarget = null;
        EmitTargetCleared();
    }

    private void OnResourcesReady(PathfindingResourceCoordinator.Snapshot _)
    {
        if (_cursor.Stage != RouteStage.Following) TryRetarget();
    }

    // ── route ────────────────────────────────────────────────────────────────────

    private void OnRouteChanged()
    {
        if (_cursor.Sync(_route.Wards)) OnStageMoved();
    }

    /// <summary>The cursor has a new destination: a different ward, or off the wards altogether.</summary>
    private void OnStageMoved()
    {
        if (_cursor.Stage == RouteStage.Following)
        {
            HeadForWard();
            return;
        }

        // Home or free: back to choosing a structure, which is the core. Railroaded or not decides
        // whether a turret on the way can pull it off — Nearby() reads that.
        Waypoint = null;
        TryRetarget();
    }

    /// <summary>
    /// Emit the cursor's ward as the destination. A ward with no way to reach it — on an island the
    /// enemy cannot get to — is skipped rather than walked at forever; placement keeps towers off
    /// the route, so this is terrain, and rare.
    /// </summary>
    private void HeadForWard()
    {
        // A resolver job still in flight would land later and overwrite the ward.
        if (_owner != null) EnemyPathfindService.Instance?.Cancel(_owner.GetInstanceId());
        CurrentTarget = null;

        while (_cursor.Stage == RouteStage.Following && !Reachable(_route.CentreOf(_cursor.Target)))
            _cursor.Advance();

        if (_cursor.Stage != RouteStage.Following)
        {
            OnStageMoved();
            return;
        }

        Waypoint = _route.CentreOf(_cursor.Target);
        _announceWaypoint = true;
    }

    /// <summary>Does a walked path from here end at this point? Before the first bake, assume so.</summary>
    private bool Reachable(Vector2 point)
    {
        Rid map = GetViewport()?.World2D?.NavigationMap ?? default;
        if (!map.IsValid || _owner == null) return true;
        if (NavigationServer2D.MapGetIterationId(map) == 0) return true;   // nothing baked yet

        Vector2[] path = NavigationServer2D.MapGetPath(map, _owner.GlobalPosition, point, true);
        if (path.Length == 0) return false;

        return path[^1].DistanceTo(point) <= WardArriveRadius;
    }

    /// <summary>
    /// Single entry point for retargeting. Resets the cadence timer and submits
    /// only when the coordinator has a published snapshot (both nav + reach
    /// settled at the latest version) and no prior job is in flight; otherwise
    /// the next ResourcesReady signal (or next <see cref="Tick"/> once the job
    /// drains) will retry.
    /// </summary>
    private void TryRetarget()
    {
        _retargetTimer = TargetUpdateInterval;

        if (_coordinator == null || !_coordinator.TryGetSnapshot(out var snapshot)) return;

        var pathfindService = EnemyPathfindService.Instance;
        if (pathfindService == null || _owner == null) return;
        if (pathfindService.HasJob(_owner.GetInstanceId())) return;

        SubmitRetarget(snapshot);
    }

    private void SubmitRetarget(PathfindingResourceCoordinator.Snapshot snapshot)
    {
        var pathfindService = EnemyPathfindService.Instance;
        if (pathfindService == null || string.IsNullOrEmpty(TargetGroup)) return;
        if (_footprints == null || _owner == null) return;

        Viewport viewport = GetViewport();
        Vector2 enemyPos = _owner.GlobalPosition;

        // Break off for something close before considering the real objective. Sticky by
        // construction rather than by a flag: the enemy is walking toward the thing that pulled
        // it, so it stays inside the radius until the structure falls, and only then does the
        // objective come back. No aggro timer, no oscillation at the edge of the circle.
        List<ApproachCandidate> candidates = Nearby(AggroGroup, enemyPos, viewport, AggroRadius);

        if (candidates.Count == 0)
            candidates = Nearby(TargetGroup, enemyPos, viewport, 0f);
        candidates.Sort((a, b) =>
            enemyPos.DistanceSquaredTo(a.TowerPosition)
                .CompareTo(enemyPos.DistanceSquaredTo(b.TowerPosition)));

        float standoff = Mathf.Max(GlobalEnemyConfig.AgentRadius, AttackRange);
        pathfindService.Submit(_owner.GetInstanceId(), enemyPos, snapshot.NavMap, standoff,
            candidates, snapshot.Reach);
    }

    /// <summary>
    /// Candidates from a group, nearest first. A <paramref name="radius"/> of 0 means no limit.
    /// Anything without a footprint is skipped — the approach resolver stands a target off from
    /// its footprint, so a structure that never registered one cannot be approached.
    /// </summary>
    private List<ApproachCandidate> Nearby(string group, Vector2 from, Viewport viewport, float radius)
    {
        List<ApproachCandidate> found = [];

        if (string.IsNullOrEmpty(group)) return found;
        if (radius > 0 && Railroaded) return found;   // believing the route means walking past

        float radiusSquared = radius * radius;

        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is not Node2D n2d || n2d.GetViewport() != viewport) continue;
            if (radius > 0 && from.DistanceSquaredTo(n2d.GlobalPosition) > radiusSquared) continue;
            if (!_footprints.TryGetFootprint(n2d, out var footprint)) continue;

            found.Add(new ApproachCandidate(n2d.GetInstanceId(), n2d.GlobalPosition, footprint));
        }

        return found;
    }

    private void DrainPendingPathResult()
    {
        var pathfindService = EnemyPathfindService.Instance;
        if (pathfindService == null || _owner == null) return;
        if (!pathfindService.TryConsume(_owner.GetInstanceId(), out ApproachResult result, out ulong ageMs)) return;
        if (ageMs > (ulong)MaxResultAgeMs) return;

        if (!result.Found)
        {
            ClearTarget();
            return;
        }

        if (InstanceFromId(result.TowerInstanceId) is Node2D tower && IsInstanceValid(tower))
        {
            CurrentTarget = tower;
            EmitApproachResolved(result.Approach);
        }
        else
        {
            ClearTarget();
        }
    }
}
