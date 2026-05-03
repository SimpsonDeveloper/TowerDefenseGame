using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Selects which tower the enemy walks toward and emits the navmesh-validated
/// approach point for an <see cref="EnemyNavController"/> (or any consumer) to
/// follow.
///
/// Targeting strategy (per retarget):
///   1. Snapshot scene-graph state on the main thread: enemy position,
///      candidate towers (Euclidean-sorted) + footprint refs, nav map RID.
///   2. Submit the snapshot to <see cref="EnemyPathfindService"/>, which runs
///      <see cref="EnemyApproachResolver"/> on a worker thread.
///   3. On a later <see cref="Tick"/>, drain the result, validate the chosen
///      tower is still alive, and raise <see cref="ApproachResolved"/> with the
///      destination — or <see cref="TargetCleared"/> if no tower is reachable.
///
/// The targeter never touches movement state; the mover subscribes and decides
/// what to do with the destination.
/// </summary>
[GlobalClass]
public partial class EnemyTowerTargeter : Node
{
    [Export] public string TargetGroup { get; set; } = "Towers";
    [Export] public EnemyConfig EnemyConfig { get; set; }

    /// <summary>
    /// Reach of the enemy's attack. Added to agentRadius to form the standoff
    /// distance from the tower footprint at which the approach path is cut
    /// short. Set to 0 for strict melee-on-contact.
    /// </summary>
    [Export] public float AttackRange { get; set; } = 0f;

    /// <summary>
    /// Seconds between retargets. Each retarget runs two MapGetPath queries
    /// (one for reachability, one for the agent path), so keep above 0.1s.
    /// </summary>
    [Export] public float TargetUpdateInterval { get; set; } = 0.25f;

    /// <summary>
    /// Maximum age, in milliseconds, of a path-resolve result before it's
    /// discarded. If the worker is backlogged and the result lands too late,
    /// the enemy state has moved enough that the approach point is no longer
    /// valid; better to drop it and let the next retarget cycle resubmit.
    /// </summary>
    [Export] public int MaxResultAgeMs { get; set; } = 500;

    /// <summary>Fires when a fresh, navmesh-validated approach point is ready.</summary>
    public event Action<Vector2> ApproachResolved;

    /// <summary>Fires when the current target became invalid or no tower is reachable.</summary>
    public event Action TargetCleared;

    public Node2D CurrentTarget { get; private set; }

    public float DistanceToTarget =>
        CurrentTarget != null && _owner != null
            ? _owner.GlobalPosition.DistanceTo(CurrentTarget.GlobalPosition)
            : float.MaxValue;

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
    public void Tick(double delta)
    {
        DrainPendingResult();

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
        TargetCleared?.Invoke();
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

        var service = EnemyPathfindService.Instance;
        if (service == null || _owner == null) return;
        if (service.HasJob(_owner.GetInstanceId())) return;

        SubmitRetarget(snapshot);
    }

    private void SubmitRetarget(PathfindingResourceCoordinator.Snapshot snapshot)
    {
        var service = EnemyPathfindService.Instance;
        if (service == null || string.IsNullOrEmpty(TargetGroup)) return;
        if (_footprints == null || _owner == null) return;

        Viewport viewport = GetViewport();
        List<ApproachCandidate> candidates = new();
        foreach (Node node in GetTree().GetNodesInGroup(TargetGroup))
        {
            if (node is not Node2D n2d || n2d.GetViewport() != viewport) continue;
            if (!_footprints.TryGetFootprint(n2d, out var footprint)) continue;
            candidates.Add(new ApproachCandidate(n2d.GetInstanceId(), n2d.GlobalPosition, footprint));
        }
        Vector2 enemyPos = _owner.GlobalPosition;
        candidates.Sort((a, b) =>
            enemyPos.DistanceSquaredTo(a.TowerPosition)
                .CompareTo(enemyPos.DistanceSquaredTo(b.TowerPosition)));

        float standoff = Mathf.Max(EnemyConfig?.AgentRadius ?? 0f, AttackRange);
        service.Submit(_owner.GetInstanceId(), enemyPos, snapshot.NavMap, standoff,
            candidates, snapshot.Reach);
    }

    private void DrainPendingResult()
    {
        var service = EnemyPathfindService.Instance;
        if (service == null || _owner == null) return;
        if (!service.TryConsume(_owner.GetInstanceId(), out ApproachResult result, out ulong ageMs)) return;
        if (ageMs > (ulong)MaxResultAgeMs) return;

        if (!result.Found)
        {
            ClearTarget();
            return;
        }

        if (InstanceFromId(result.TowerInstanceId) is Node2D tower && IsInstanceValid(tower))
        {
            CurrentTarget = tower;
            ApproachResolved?.Invoke(result.Approach);
        }
        else
        {
            ClearTarget();
        }
    }
}
