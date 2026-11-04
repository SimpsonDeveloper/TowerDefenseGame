using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.paths;

/// <summary>
/// The route the player lays down, and the only thing that draws it.
/// Design: <c>docs/tower-design/impl-planning/combat/paths.md</c>.
///
/// <b>A ward is a navigation waypoint, not a road tile.</b> This node owns an ordered list of
/// tiles; enemies carry a <c>NavigationAgent2D</c> already, so walking ward-to-ward is a sequence
/// of destinations and the existing nav does every bit of the routing between them.
///
/// <b>What is drawn is what would be walked.</b> Each leg is a real
/// <see cref="NavigationServer2D.MapGetPath(Rid, Vector2, Vector2, bool)"/> query, the same call
/// the enemies' own approach resolver makes, so the thread bends around a tower exactly where an
/// enemy would. Drawing a straight line between wards instead would be a drawing that lies: it
/// would run through towers and over water, and the player would lay a route by it that the
/// enemies never take.
///
/// Drawn immediate-mode, like the HP bar and the state readout: one node renders the whole route
/// rather than a node per ward. A ward is two numbers, and giving each one a scene tree entry
/// would cost more than it draws.
/// </summary>
[GlobalClass]
public partial class MiragePath : Node2D
{
    [Export] public CoordConfig Coords { get; set; }

    /// <summary>Towers. Needed to refuse a ward on an occupied tile — nav alone would allow it.</summary>
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }

    /// <summary>Purple, matching the Mind bar — the path is a Mind attack, so it shares the meter's colour.</summary>
    [Export] public Color Mirage { get; set; } = new("a974ff");

    /// <summary>The lit core of a ward the player placed.</summary>
    [Export] public Color Core { get; set; } = new("6fd3ff");

    /// <summary>A leg with nowhere to walk, and a ward that cannot be laid.</summary>
    [Export] public Color Blocked { get; set; } = new("ff5a4a");

    /// <summary>
    /// The dark ring either side of the purple. Not decoration: the terrain is four colour
    /// variants at 4px blocks, and without a hard outline the glyph dissolves into that noise.
    /// </summary>
    [Export] public Color Outline { get; set; } = new("0a0f14");

    /// <summary>Tiles between the unlit rings drawn along the thread.</summary>
    [Export] public int MinorEveryTiles { get; set; } = 4;

    /// <summary>Pixels between thread pips.</summary>
    [Export] public float DotSpacing { get; set; } = 6f;

    /// <summary>Pixels per second the thread's pips drift toward the core.</summary>
    [Export] public float DriftSpeed { get; set; } = 8f;

    /// <summary>Core pulses per second. Zero holds them lit.</summary>
    [Export] public float PulseRate { get; set; } = 0.5f;

    /// <summary>
    /// Seconds between route re-queries. The nav mesh bakes <b>asynchronously</b> after a tower
    /// lands, so there is no moment at which recomputing once would be right; polling slowly is
    /// honest about that and costs one path query per leg a few times a second.
    /// </summary>
    [Export] public double RerouteInterval { get; set; } = 0.25;

    /// <summary>The route, in placement order. Tiles, not pixels.</summary>
    public IReadOnlyList<Vector2I> Wards => _wards;

    /// <summary>True when any leg of the route has no walkable path. Readouts and placement.</summary>
    public bool IsBroken { get; private set; }

    private readonly List<Vector2I> _wards = new();

    /// <summary>One entry per leg: the walked polyline, or empty where the leg is blocked.</summary>
    private readonly List<Vector2[]> _legs = new();

    private Vector2I? _preview;
    private bool _previewValid;
    private Vector2[] _previewLeg = Array.Empty<Vector2>();

    private double _time;
    private double _untilReroute;

    /// <summary>The route changed. The placement UI and, later, enemies in flight care.</summary>
    public event Action Changed;

    public override void _Ready()
    {
        ZIndex = -1;   // under towers and enemies: the path is ground marking, not an actor
        if (Coords == null) GD.PushWarning($"{Name}: no CoordConfig — the path cannot place itself.");
    }

    // ── route ────────────────────────────────────────────────────────────────────

    /// <summary>Add a ward at the end of the route. Refused where <see cref="CanWard"/> says so.</summary>
    public bool Add(Vector2I tile)
    {
        if (!CanWard(tile)) return false;

        _wards.Add(tile);
        Touch();
        return true;
    }

    /// <summary>
    /// Remove the ward on this tile. The route <b>rejoins</b> rather than splitting: there is
    /// exactly one route and it is ordered, so a path with a hole in it has no meaning.
    /// </summary>
    public bool Remove(Vector2I tile)
    {
        if (!_wards.Remove(tile)) return false;

        Touch();
        return true;
    }

    public bool HasWard(Vector2I tile) => _wards.Contains(tile);

    /// <summary>
    /// May a ward go here? A tower's tile is refused outright: the tower is solid, the nav mesh is
    /// cut away under it, and a waypoint inside one is a destination no enemy can stand on.
    /// Unwalkable ground — water, rock — is refused for the same reason.
    /// </summary>
    public bool CanWard(Vector2I tile)
    {
        if (_wards.Contains(tile)) return false;
        if (FootprintTracker != null && FootprintTracker.IsOccupied(tile)) return false;

        return IsWalkable(CentreOf(tile));
    }

    /// <summary>Does this route pass over the given tile — ward or walked thread? Tower placement asks.</summary>
    public bool Crosses(Vector2I tile)
    {
        if (_wards.Contains(tile)) return true;

        int size = Coords?.TilePixelSize ?? 16;
        Rect2 cell = new(CoordHelper.TileToWorld(tile, Coords), new Vector2(size, size));

        foreach (Vector2[] leg in _legs)
            for (int i = 0; i < leg.Length - 1; i++)
                if (SegmentHitsRect(leg[i], leg[i + 1], cell)) return true;

        return false;
    }

    public void Clear()
    {
        if (_wards.Count == 0) return;

        _wards.Clear();
        Touch();
    }

    /// <summary>World-space centre of a ward, for anything navigating to it.</summary>
    public Vector2 CentreOf(Vector2I tile)
    {
        int size = Coords?.TilePixelSize ?? 16;
        return CoordHelper.TileToWorld(tile, Coords) + new Vector2(size, size) * 0.5f;
    }

    // ── preview ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Show where a ward would land and what it would do to the route. Driven by
    /// <c>TowerPlacementManager</c> while in ward mode; pass <c>null</c> to clear it.
    ///
    /// The preview routes for real, so the player sees the thread bend around a tower <i>before</i>
    /// committing rather than after.
    /// </summary>
    public void SetPreview(Vector2I? tile, bool valid)
    {
        if (_preview == tile && _previewValid == valid) return;

        _preview = tile;
        _previewValid = valid;
        _previewLeg = Array.Empty<Vector2>();

        if (tile.HasValue && _wards.Count > 0)
            _previewLeg = Route(CentreOf(_wards[^1]), CentreOf(tile.Value));

        QueueRedraw();
    }

    // ── navigation ───────────────────────────────────────────────────────────────

    private Rid NavMap => GetViewport()?.World2D?.NavigationMap ?? default;

    /// <summary>The walked polyline between two points, or empty where there is no way through.</summary>
    private Vector2[] Route(Vector2 from, Vector2 to)
    {
        Rid map = NavMap;
        if (!map.IsValid) return Array.Empty<Vector2>();

        Vector2[] points = NavigationServer2D.MapGetPath(map, from, to, true);
        return points is { Length: >= 2 } ? points : Array.Empty<Vector2>();
    }

    /// <summary>
    /// Is there nav mesh under this point? Asked by snapping to the nearest mesh point and seeing
    /// whether it moved — a point off the mesh snaps somewhere else, one on it stays put.
    /// </summary>
    private bool IsWalkable(Vector2 world)
    {
        Rid map = NavMap;
        if (!map.IsValid) return true;   // before the first bake, refuse nothing

        int size = Coords?.TilePixelSize ?? 16;
        return NavigationServer2D.MapGetClosestPoint(map, world).DistanceTo(world) < size * 0.5f;
    }

    private void Rebuild()
    {
        _legs.Clear();
        IsBroken = false;

        for (int i = 0; i < _wards.Count - 1; i++)
        {
            Vector2[] leg = Route(CentreOf(_wards[i]), CentreOf(_wards[i + 1]));
            _legs.Add(leg);
            if (leg.Length == 0) IsBroken = true;
        }

        QueueRedraw();
    }

    private void Touch()
    {
        Rebuild();
        Changed?.Invoke();
    }

    // ── drawing ──────────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_wards.Count == 0 && !_preview.HasValue) return;

        _time += delta;

        _untilReroute -= delta;
        if (_untilReroute <= 0)
        {
            _untilReroute = RerouteInterval;
            Rebuild();
            if (_preview.HasValue) SetPreview(_preview, _previewValid);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        for (int i = 0; i < _legs.Count; i++)
            DrawLeg(_legs[i], CentreOf(_wards[i]), CentreOf(_wards[i + 1]), Mirage);

        foreach (Vector2I ward in _wards)
            DrawRing(CentreOf(ward), lit: true, phase: _wards.IndexOf(ward), tint: Mirage);

        if (!_preview.HasValue) return;

        Color tint = _previewValid ? Core : Blocked;

        if (_wards.Count > 0)
            DrawLeg(_previewLeg, CentreOf(_wards[^1]), CentreOf(_preview.Value), tint);

        DrawRing(CentreOf(_preview.Value), lit: true, phase: 0, tint: tint);
    }

    /// <summary>
    /// One leg: pips along the walked polyline, plus an unlit ring every few tiles so a long
    /// stretch still reads as a warded path rather than a dotted line. An empty polyline is a leg
    /// with no way through — drawn straight and in the blocked colour, because the player needs to
    /// see the break and where it runs.
    /// </summary>
    private void DrawLeg(Vector2[] leg, Vector2 from, Vector2 to, Color tint)
    {
        if (leg.Length < 2)
        {
            DrawDashedStraight(from, to);
            return;
        }

        float drift = DriftSpeed > 0 ? (float)(_time * DriftSpeed) % DotSpacing : 0f;
        WalkPolyline(leg, DotSpacing, drift, p =>
        {
            DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(4, 4)), Outline);
            DrawRect(new Rect2(p - new Vector2(1, 1), new Vector2(2, 2)), tint);
        });

        // Pinned to distance along the leg, not to the drifting pips, so they stay put while the
        // thread runs past them.
        int tileSize = Coords?.TilePixelSize ?? 16;
        float minorEvery = Mathf.Max(MinorEveryTiles, 1) * tileSize;
        WalkPolyline(leg, minorEvery, minorEvery, p => DrawRing(p, lit: false, phase: 0, tint: tint));
    }

    /// <summary>A broken leg: a bare red dash straight across, which is not a route and should not look like one.</summary>
    private void DrawDashedStraight(Vector2 from, Vector2 to)
    {
        float length = from.DistanceTo(to);
        if (length <= 0.001f) return;

        Vector2 step = (to - from) / length;
        for (float d = 0; d < length; d += 8f)
        {
            Vector2 p = (from + step * d).Round();
            DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(4, 4)), Outline);
            DrawRect(new Rect2(p - new Vector2(1, 1), new Vector2(3, 1)), Blocked);
        }
    }

    /// <summary>Step along a polyline at a fixed arc length, so spacing survives every corner.</summary>
    private static void WalkPolyline(Vector2[] points, float interval, float offset, Action<Vector2> at)
    {
        if (interval <= 0 || points.Length < 2) return;

        float carry = offset;
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float length = a.DistanceTo(b);
            if (length <= 0.001f) continue;

            Vector2 step = (b - a) / length;
            for (float d = carry; d < length; d += interval)
                at((a + step * d).Round());

            // Carry the remainder into the next segment instead of restarting the count at the
            // corner, which would bunch pips wherever the route turns.
            carry = ((carry - length) % interval + interval) % interval;
        }
    }

    /// <summary>
    /// One ward: a hollow 8×8 square, sandwiched between two dark rings, with a lit core if the
    /// player placed it. Hollow on purpose — the ground's noise makes blocks and never outlines,
    /// so an outline is the one shape it cannot imitate, and the mark never hides the tile beneath.
    /// </summary>
    private void DrawRing(Vector2 centre, bool lit, int phase, Color tint)
    {
        Vector2 o = centre - new Vector2(8, 8);   // back to the tile's top-left

        Box(o, 3, 3, 10, 1, Outline);
        Box(o, 3, 12, 10, 1, Outline);
        Box(o, 3, 3, 1, 10, Outline);
        Box(o, 12, 3, 1, 10, Outline);

        Box(o, 5, 5, 6, 1, Outline);
        Box(o, 5, 10, 6, 1, Outline);
        Box(o, 5, 5, 1, 6, Outline);
        Box(o, 10, 5, 1, 6, Outline);

        Box(o, 4, 4, 8, 1, tint);
        Box(o, 4, 11, 8, 1, tint);
        Box(o, 4, 4, 1, 8, tint);
        Box(o, 11, 4, 1, 8, tint);

        if (!lit) return;

        Box(o, 6, 6, 4, 4, Outline);
        Box(o, 7, 7, 2, 2, Pulsing(phase) ? Colors.White : Core);
    }

    /// <summary>Offset per ward, so a long route shimmers along its length instead of blinking as one.</summary>
    private bool Pulsing(int phase) =>
        PulseRate > 0 && Mathf.Sin((float)_time * PulseRate * Mathf.Tau - phase * 0.6f) > 0.4f;

    private void Box(Vector2 origin, int x, int y, int w, int h, Color color) =>
        DrawRect(new Rect2(origin + new Vector2(x, y), new Vector2(w, h)), color);

    /// <summary>Does a segment touch a tile's cell? Slab clip — no allocation, no trig.</summary>
    private static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect2 cell)
    {
        if (cell.HasPoint(a) || cell.HasPoint(b)) return true;

        Vector2 d = b - a;
        float t0 = 0f, t1 = 1f;
        float[] p = { -d.X, d.X, -d.Y, d.Y };
        float[] q =
        {
            a.X - cell.Position.X, cell.End.X - a.X,
            a.Y - cell.Position.Y, cell.End.Y - a.Y
        };

        for (int i = 0; i < 4; i++)
        {
            if (Mathf.IsZeroApprox(p[i]))
            {
                if (q[i] < 0) return false;
                continue;
            }

            float r = q[i] / p[i];
            if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else          { if (r < t0) return false; if (r < t1) t1 = r; }
        }

        return true;
    }
}
