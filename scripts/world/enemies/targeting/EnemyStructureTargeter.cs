using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;

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
    /// While true the enemy cannot break off, whatever it walks past — it is following a route it
    /// believes in. Set by the path follower once that exists; a <b>deviating</b> enemy clears it
    /// and becomes free to turn on a turret beside it
    /// (<c>impl-planning/combat/paths.md</c>). Nothing writes it yet, so every enemy is free.
    /// </summary>
    public bool Railroaded { get; set; }

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

    public override void _Ready()
    {
        _owner = GetParent<Node2D>();
        Viewport viewport = GetViewport();
        _coordinator = PathfindingResourceCoordinator.ForViewport(viewport);
        _footprints  = TowerFootprintTracker.ForViewport(viewport);
        if (_coordinator != null)
            _coordinator.ResourcesReady += OnResourcesReady;

        TryRetarget();
    }

    public override void _ExitTree()
    {
        if (_coordinator != null)
            _coordinator.ResourcesReady -= OnResourcesReady;

        if (_owner != null)
            EnemyPathfindService.Instance?.Cancel(_owner.GetInstanceId());
    }

    /// <summary>
    /// Driven by the owning controller in physics order so drain happens before
    /// movement consumes the destination on the same tick.
    /// </summary>
    public override void Tick(double delta)
    {
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

    private void OnResourcesReady(PathfindingResourceCoordinator.Snapshot _) => TryRetarget();

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
