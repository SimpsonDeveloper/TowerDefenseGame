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
/// Thread-safety: takes no scene-graph references — only positions, footprint
/// snapshots, and the nav map RID. <see cref="TowerFootprint"/> is immutable
/// and <see cref="NavigationServer2D"/> queries are thread-safe, so this can
/// run on a worker thread.
/// </summary>
public static class EnemyApproachResolver
{
    public static ApproachResult Resolve(
        Vector2 enemyPos, Rid navMap, float standoff,
        IReadOnlyList<ApproachCandidate> candidatesByDistance)
    {
        if (!navMap.IsValid) return ApproachResult.Miss;
        float standoffSq = standoff * standoff;

        for (int c = 0; c < candidatesByDistance.Count; c++)
        {
            var cand = candidatesByDistance[c];
            if (TryResolveForCandidate(enemyPos, navMap, standoff, standoffSq, cand, out Vector2 approach))
                return new ApproachResult(cand.TowerInstanceId, approach);
        }
        return ApproachResult.Miss;
    }

    /// <summary>
    /// Runs the two MapGetPath queries (Euclidean-closest edge + tower position),
    /// scores each, and returns the shorter approach. Returns false when neither
    /// path enters the standoff zone.
    /// </summary>
    private static bool TryResolveForCandidate(
        Vector2 enemyPos, Rid navMap, float standoff, float standoffSq,
        ApproachCandidate cand, out Vector2 approach)
    {
        approach = default;
        TowerFootprint footprint = cand.Footprint;
        if (footprint == null) return false;

        float bestLen = float.MaxValue;
        bool found = false;

        // Query A: path to the Euclidean-closest reachable footprint edge.
        if (footprint.TryFindApproachDestination(enemyPos, standoff, navMap, out Vector2 euclidDest))
        {
            Vector2[] euclidPath = NavigationServer2D.MapGetPath(navMap, enemyPos, euclidDest, true);
            if (TryScorePath(euclidPath, footprint, standoffSq, out Vector2 a, out float len)
                && len < bestLen)
            {
                bestLen = len; approach = a; found = true;
            }
        }

        // Query B: path to the tower node's position; MapGetPath snaps it to the
        // navmesh, so the path enters the standoff zone at the path-shortest side.
        Vector2[] towerPath = NavigationServer2D.MapGetPath(navMap, enemyPos, cand.TowerPosition, true);
        if (TryScorePath(towerPath, footprint, standoffSq, out Vector2 b, out float lenB)
            && lenB < bestLen)
        {
            approach = b; found = true;
        }

        return found;
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
