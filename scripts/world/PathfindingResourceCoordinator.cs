using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Compounds the navmesh and the reachability index into a single "world is
/// stable" signal for pathfinding consumers. Owns a monotonically increasing
/// version that bumps on every tower placement / removal; only publishes a
/// <see cref="Snapshot"/> when both resources have completed a bake at the
/// current version.
///
/// Subscribes to <see cref="TowerPlacementManager"/> in <c>_EnterTree</c> so it
/// runs ahead of the resource managers (which subscribe in <c>_Ready</c>) and
/// can bump version before either bake captures its own in-flight version-tag.
///
/// All flag manipulation is guarded by a single lock; the <see cref="ResourcesReady"/>
/// event fires after the lock is released, with the snapshot captured locally.
/// </summary>
[GlobalClass]
public partial class PathfindingResourceCoordinator : Node
{
    [Export] public PocketNavGridManager NavGrid { get; set; }
    [Export] public PocketReachabilityIndex Reach { get; set; }
    [Export] public TowerPlacementManager TowerPlacement { get; set; }

    /// <summary>Per-viewport registry, mirrors the resource managers so consumers
    /// can resolve their coordinator by their own viewport.</summary>
    private static readonly Dictionary<Viewport, PathfindingResourceCoordinator> ByViewport = new();

    public static PathfindingResourceCoordinator ForViewport(Viewport viewport)
        => viewport != null && ByViewport.TryGetValue(viewport, out var c) ? c : null;

    /// <summary>Immutable, internally-consistent view of both pathfinding
    /// resources at a single version. Safe to use from any thread.</summary>
    public readonly struct Snapshot
    {
        public PocketReachabilityIndex.Snapshot Reach { get; }
        public Rid NavMap { get; }
        public int Version { get; }

        internal Snapshot(PocketReachabilityIndex.Snapshot reach, Rid navMap, int version)
        {
            Reach = reach;
            NavMap = navMap;
            Version = version;
        }
    }

    /// <summary>Fires (main thread, after the lock is released) every time both
    /// resources settle at the latest requested version. Multiple consecutive
    /// fires are possible if rebakes chain; the snapshot is always at the
    /// version current at publish time.</summary>
    public event Action<Snapshot> ResourcesReady;

    // ── Lock-guarded state ──────────────────────────────────────────────────────

    private readonly object _lock = new();
    private int  _requestedVersion;        // bumped on every tower change
    private bool _navInFlight;
    private int  _navInFlightVersion;       // version the in-flight nav batch will cover
    private int  _navCompletedVersion = -1;
    private bool _reachInFlight;
    private int  _reachInFlightVersion;     // version the in-flight reach bake will cover
    private int  _reachCompletedVersion = -1;
    private Snapshot? _lastReady;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    public override void _EnterTree()
    {
        Viewport vp = GetViewport();
        if (vp != null)
        {
            if (ByViewport.TryGetValue(vp, out var existing) && existing != this)
                GD.PushWarning($"{Name}: another PathfindingResourceCoordinator already registered for this viewport.");
            else
                ByViewport[vp] = this;
        }

        // Subscribe in _EnterTree so we run ahead of the resource managers'
        // _Ready subscriptions — we must bump _requestedVersion before either
        // manager fires its BakeStarted event in response to the same TowerPlaced.
        if (TowerPlacement != null)
        {
            TowerPlacement.TowerPlaced  += OnTowerChanged;
            TowerPlacement.TowerRemoved += OnTowerChanged;
        }
        if (NavGrid != null)
        {
            NavGrid.BakingStarted  += OnNavBakingStarted;
            NavGrid.BakingComplete += OnNavBakingComplete;
        }
        if (Reach != null)
        {
            Reach.BakeStarted        += OnReachBakeStarted;
            Reach.ReachabilityReady  += OnReachReady;
        }
    }

    public override void _ExitTree()
    {
        if (TowerPlacement != null)
        {
            TowerPlacement.TowerPlaced  -= OnTowerChanged;
            TowerPlacement.TowerRemoved -= OnTowerChanged;
        }
        if (NavGrid != null)
        {
            NavGrid.BakingStarted  -= OnNavBakingStarted;
            NavGrid.BakingComplete -= OnNavBakingComplete;
        }
        if (Reach != null)
        {
            Reach.BakeStarted        -= OnReachBakeStarted;
            Reach.ReachabilityReady  -= OnReachReady;
        }

        Viewport vp = GetViewport();
        if (vp != null && ByViewport.TryGetValue(vp, out var c) && c == this)
            ByViewport.Remove(vp);
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>True iff a snapshot has ever been published. Once true, stays true;
    /// the most recent snapshot keeps serving on-demand queries even while a
    /// fresh bake is in flight.</summary>
    public bool IsReady
    {
        get { lock (_lock) return _lastReady.HasValue; }
    }

    /// <summary>Captures the most recently published snapshot for on-demand use
    /// (e.g. timer-driven pathfinding between ResourcesReady events). Returns
    /// false until the first publish; once true, never reverts to false.</summary>
    public bool TryGetSnapshot(out Snapshot snapshot)
    {
        lock (_lock)
        {
            if (_lastReady is Snapshot s) { snapshot = s; return true; }
        }
        snapshot = default;
        return false;
    }

    // ── Event handlers (all main-thread; lock guards against future workers) ───

    private void OnTowerChanged(IReadOnlyList<Vector2I> _)
    {
        lock (_lock)
        {
            _requestedVersion++;
            // The navmesh batch absorbs mid-batch tower changes (re-queues the
            // affected cell), so the in-flight version follows the latest request.
            // The reach bake snapshots its input at start, so an in-flight bake
            // will NOT include this change — its version stays put; the dirty
            // flag on the reach manager will trigger a follow-up bake whose
            // BakeStarted will capture the new version.
            if (_navInFlight) _navInFlightVersion = _requestedVersion;
        }
    }

    private void OnNavBakingStarted()
    {
        lock (_lock)
        {
            _navInFlight = true;
            _navInFlightVersion = _requestedVersion;
        }
    }

    private void OnNavBakingComplete()
    {
        Snapshot? toFire;
        lock (_lock)
        {
            _navInFlight = false;
            _navCompletedVersion = _navInFlightVersion;
            toFire = TryPublishLocked();
        }
        if (toFire is Snapshot s) ResourcesReady?.Invoke(s);
    }

    private void OnReachBakeStarted()
    {
        lock (_lock)
        {
            _reachInFlight = true;
            _reachInFlightVersion = _requestedVersion;
        }
    }

    private void OnReachReady()
    {
        Snapshot? toFire;
        lock (_lock)
        {
            _reachInFlight = false;
            _reachCompletedVersion = _reachInFlightVersion;
            toFire = TryPublishLocked();
        }
        if (toFire is Snapshot s) ResourcesReady?.Invoke(s);
    }

    /// <summary>Caller MUST hold <c>_lock</c>. Returns a snapshot iff both
    /// resources have completed a bake at the current requested version and
    /// neither has a follow-up in flight; otherwise null.</summary>
    private Snapshot? TryPublishLocked()
    {
        if (_navInFlight || _reachInFlight) return null;
        if (_navCompletedVersion   != _requestedVersion) return null;
        if (_reachCompletedVersion != _requestedVersion) return null;

        if (Reach == null || !Reach.TryAcquireSnapshot(out var reachSnap)) return null;

        Rid navMap = GetViewport()?.World2D?.NavigationMap ?? default;
        var snap = new Snapshot(reachSnap, navMap, _requestedVersion);
        _lastReady = snap;
        return snap;
    }
}
