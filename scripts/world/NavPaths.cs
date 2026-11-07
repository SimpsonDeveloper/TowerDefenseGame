using System;
using System.Collections.Generic;
using Godot;

namespace towerdefensegame.scripts.world;

/// <summary>
/// The path an enemy walks between two points, and the one the route draws. One function, so the
/// two cannot disagree.
///
/// <b>Why not <c>MapGetPath</c> alone.</b> Godot's path query is an A* over nav <i>polygons</i>,
/// followed by a funnel that pulls the line tight inside whichever strip of polygons A* chose. The
/// funnel is exact; the strip is not. A* costs each polygon by a single entry point on its edge,
/// so with large triangles it can pick a strip that is not the shortest, and the funnel then
/// faithfully hugs that strip's corner. The pocket mesh makes this common: it is baked as a grid
/// of separate 512px regions, each triangulated on its own into long fans, and the corners where
/// four regions meet are exactly where the wrong strip bends a path that should be straight.
///
/// <b>The fix is a line-of-sight pass.</b> From each kept point, jump to the furthest later point
/// it can see across the mesh, and drop everything between. It only removes corners, never adds
/// them, so it cannot make a path longer or route it anywhere the raw path could not go — and
/// "can see" is asked of the nav mesh itself, which is already eroded by the agent radius, so a
/// shortcut is as safe to walk as the corners it replaced.
///
/// Main thread only: the visibility test is a run of <c>MapGetClosestPoint</c> queries.
/// </summary>
public static class NavPaths
{
    /// <summary>
    /// Pixels between visibility samples along a shortcut. A quarter tile: the mesh is eroded by
    /// the agent radius, which is wider than this, so a gap between samples cannot hide a wall.
    /// </summary>
    private const float SampleStep = 4f;

    /// <summary>How far a sample may sit from the mesh and still count as on it — float slop only.</summary>
    private const float OnMeshTolerance = 0.5f;

    /// <summary>The walked path from <paramref name="from"/> to <paramref name="to"/>, corners the mesh does not need removed.</summary>
    public static Vector2[] Find(Rid map, Vector2 from, Vector2 to)
    {
        if (!map.IsValid) return Array.Empty<Vector2>();

        Vector2[] raw = NavigationServer2D.MapGetPath(map, from, to, true);
        return Straighten(map, raw);
    }

    /// <summary>Greedy string-pull over the raw path's own points. Two points or fewer are already straight.</summary>
    public static Vector2[] Straighten(Rid map, Vector2[] raw)
    {
        if (raw.Length <= 2) return raw;

        List<Vector2> kept = new() { raw[0] };
        int at = 0;

        while (at < raw.Length - 1)
        {
            int next = raw.Length - 1;
            while (next > at + 1 && !Sees(map, raw[at], raw[next])) next--;

            kept.Add(raw[next]);
            at = next;
        }

        return kept.ToArray();
    }

    /// <summary>Does the straight segment between two points stay on the mesh the whole way?</summary>
    public static bool Sees(Rid map, Vector2 a, Vector2 b)
    {
        float length = a.DistanceTo(b);
        if (length <= SampleStep) return true;

        Vector2 step = (b - a) / length * SampleStep;
        int samples = (int)(length / SampleStep);
        float tolerance = OnMeshTolerance * OnMeshTolerance;

        // Endpoints are path points, already on the mesh; only the span between needs asking.
        for (int i = 1; i <= samples; i++)
        {
            Vector2 p = a + step * i;
            if (NavigationServer2D.MapGetClosestPoint(map, p).DistanceSquaredTo(p) > tolerance)
                return false;
        }

        return true;
    }
}
