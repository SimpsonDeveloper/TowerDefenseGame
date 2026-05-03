using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>Tower candidate snapshot for approach resolution. Captures only
/// the data the resolver needs, so a snapshot can outlive the source Node
/// (e.g. cross a thread boundary). The instance ID lets the caller correlate
/// the result back to the live <c>Node2D</c> after the resolver returns.</summary>
public readonly struct ApproachCandidate
{
    public readonly ulong TowerInstanceId;
    public readonly Vector2 TowerPosition;
    public readonly TowerFootprint Footprint;

    public ApproachCandidate(ulong towerInstanceId, Vector2 towerPosition, TowerFootprint footprint)
    {
        TowerInstanceId = towerInstanceId;
        TowerPosition = towerPosition;
        Footprint = footprint;
    }
}

/// <summary>Outcome of <see cref="EnemyApproachResolver.Resolve"/>. <see cref="Found"/>
/// is false when no candidate yielded a viable approach.</summary>
public readonly struct ApproachResult
{
    public readonly bool Found;
    public readonly ulong TowerInstanceId;
    public readonly Vector2 Approach;

    public ApproachResult(ulong towerInstanceId, Vector2 approach)
    {
        Found = true;
        TowerInstanceId = towerInstanceId;
        Approach = approach;
    }

    public static ApproachResult Miss => default;
}

/// <summary>
/// Pure-data approach resolver. Iterates candidates in the supplied order
/// (caller pre-sorts — typically by Euclidean distance to the enemy); for
/// each, runs the dual-query strategy used by <see cref="EnemyTowerTargeter"/>
/// and short-circuits on the first candidate that yields an approach point.
///
/// When a <see cref="PocketReachabilityIndex.Snapshot"/> is supplied, candidates
/// whose outward neighbors are all in a different connected component than
/// the enemy are fast-rejected without running any nav queries, and individual
/// snap candidates within the surviving footprint scan are gated on the same
/// component test. NOTE: this assumes melee/contact attackers — ranged units
/// that attack across gaps would need a different gate (range + LOS instead
/// of component match).
///
/// Thread-safety: takes no scene-graph references — only positions, footprint
/// snapshots, and the nav map RID. <see cref="TowerFootprint"/> is immutable,
/// the captured probe wraps an immutable <see cref="PocketReachabilityIndex"/>
/// snapshot, and <see cref="NavigationServer2D"/> queries are thread-safe, so
/// this can run on a worker thread.
/// </summary>
public static class EnemyApproachResolver
{
    public static ApproachResult Resolve(
        Vector2 enemyPos, Rid navMap, float standoff,
        IReadOnlyList<ApproachCandidate> candidatesByDistance,
        PocketReachabilityIndex.Snapshot? probe = null)
    {
        if (!navMap.IsValid) return ApproachResult.Miss;
        float standoffSq = standoff * standoff;

        // Cache the enemy's component root once so per-candidate gates are a
        // single equality compare. If the enemy isn't on any walkable tile in
        // the index, no candidate is reachable — return Miss immediately.
        int enemyRoot = 0;
        bool gateOn = false;
        if (probe is PocketReachabilityIndex.Snapshot p)
        {
            Vector2I enemyTile = CoordHelper.WorldToTile(enemyPos, p.CoordConfig);
            int? root = p.ComponentRootAt(enemyTile);
            if (root == null) return ApproachResult.Miss;
            enemyRoot = root.Value;
            gateOn = true;
        }

        for (int c = 0; c < candidatesByDistance.Count; c++)
        {
            var cand = candidatesByDistance[c];
            if (gateOn && !TouchesEnemyComponent(cand.Footprint, probe.Value, enemyRoot))
                continue;
            if (TryResolveForCandidate(enemyPos, navMap, standoff, standoffSq,
                                       cand, probe, gateOn, enemyRoot, out Vector2 approach))
                return new ApproachResult(cand.TowerInstanceId, approach);
        }
        return ApproachResult.Miss;
    }

    /// <summary>
    /// Fast-reject: pure tile-index lookups, no nav queries. If none of the
    /// footprint's outward neighbor tiles share the enemy's component, the
    /// candidate is unreachable and the caller can skip the per-edge scan.
    /// </summary>
    private static bool TouchesEnemyComponent(
        TowerFootprint fp, PocketReachabilityIndex.Snapshot probe, int enemyRoot)
    {
        var neighbors = fp.OutwardNeighborTiles;
        for (int i = 0; i < neighbors.Count; i++)
        {
            int? root = probe.ComponentRootAt(neighbors[i]);
            if (root.HasValue && root.Value == enemyRoot) return true;
        }
        return false;
    }

    /// <summary>
    /// Runs the two MapGetPath queries (Euclidean-closest reachable edge +
    /// tower position), scores each, and returns the shorter approach. Returns
    /// false when neither path enters the standoff zone — or, when the
    /// reachability gate is on, when no snap candidate is in the enemy's
    /// component.
    /// </summary>
    private static bool TryResolveForCandidate(
        Vector2 enemyPos, Rid navMap, float standoff, float standoffSq,
        ApproachCandidate cand,
        PocketReachabilityIndex.Snapshot? probe, bool gateOn, int enemyRoot,
        out Vector2 approach)
    {
        approach = default;
        TowerFootprint footprint = cand.Footprint;
        if (footprint == null) return false;

        float bestLen = float.MaxValue;
        bool found = false;

        // Query A: walk snap candidates in Euclidean order; take the first
        // whose snap is in the enemy's component (or any snap when gate off).
        if (TryFindReachableSnap(footprint, enemyPos, navMap, standoff,
                                 probe, gateOn, enemyRoot, out Vector2 euclidDest))
        {
            Vector2[] euclidPath = NavigationServer2D.MapGetPath(navMap, enemyPos, euclidDest, true);
            if (TryScorePath(euclidPath, footprint, standoffSq, out Vector2 a, out float len)
                && len < bestLen)
            {
                bestLen = len; approach = a; found = true;
            }
        }

        // Query B: snap the tower position itself. Skip the path query if the
        // snap lands in a different component than the enemy.
        Vector2 towerSnap = NavigationServer2D.MapGetClosestPoint(navMap, cand.TowerPosition);
        if (!gateOn || SnapInEnemyComponent(towerSnap, probe.Value, enemyRoot))
        {
            Vector2[] towerPath = NavigationServer2D.MapGetPath(navMap, enemyPos, towerSnap, true);
            if (TryScorePath(towerPath, footprint, standoffSq, out Vector2 b, out float lenB)
                && lenB < bestLen)
            {
                approach = b; found = true;
            }
        }

        return found;
    }

    private static bool TryFindReachableSnap(
        TowerFootprint footprint, Vector2 enemyPos, Rid navMap, float standoff,
        PocketReachabilityIndex.Snapshot? probe, bool gateOn, int enemyRoot,
        out Vector2 snap)
    {
        foreach (Vector2 candidate in footprint.EnumerateApproachSnaps(enemyPos, navMap, standoff))
        {
            if (!gateOn || SnapInEnemyComponent(candidate, probe.Value, enemyRoot))
            {
                snap = candidate;
                return true;
            }
        }
        snap = default;
        return false;
    }

    private static bool SnapInEnemyComponent(
        Vector2 snap, PocketReachabilityIndex.Snapshot probe, int enemyRoot)
    {
        Vector2I tile = CoordHelper.WorldToTile(snap, probe.CoordConfig);
        int? root = probe.ComponentRootAt(tile);
        return root.HasValue && root.Value == enemyRoot;
    }

    /// <summary>
    /// Walks <paramref name="path"/> corners; on the first corner within standoff
    /// of <paramref name="footprint"/>, returns the bisection-refined approach
    /// point and the total polyline length up to it.
    /// </summary>
    private static bool TryScorePath(
        Vector2[] path, TowerFootprint footprint, float standoffSq,
        out Vector2 approach, out float length)
    {
        approach = default; length = 0f;
        if (path.Length == 0) return false;

        for (int i = 0; i < path.Length; i++)
        {
            if (footprint.DistanceSqTo(path[i]) <= standoffSq)
            {
                if (i == 0)
                {
                    approach = path[0];
                    return true;
                }
                approach = footprint.FindStandoffPoint(path[i - 1], path[i], standoffSq);
                length += path[i - 1].DistanceTo(approach);
                return true;
            }
            if (i > 0) length += path[i - 1].DistanceTo(path[i]);
        }
        return false;
    }
}
