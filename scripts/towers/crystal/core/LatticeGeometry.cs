using System;
using System.Collections.Generic;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// A point in lattice space, where **y grows upward with the flow** — the same direction as
/// <c>row</c>. Not a Godot <c>Vector2</c>: the core stays engine-free, so the renderer converts
/// (and flips y, because screen space grows downward).
/// </summary>
public readonly record struct LatticePoint(double X, double Y)
{
    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>
/// Where a cell actually sits on screen — the bridge between integer <c>(row, col)</c> and the
/// triangles a renderer draws or a mouse clicks. Engine-free and unit-tested, because this is
/// where a coordinate scheme quietly disagrees with itself.
///
/// The tiling: a row is one horizontal **band** of height <c>Side·√3/2</c>, and <c>col</c> steps
/// half a side across it, so ▲ and ▽ interlock (`compiler-core.md` §2). A ▲ stands on its band's
/// lower line with its apex on the upper one; a ▽ hangs from the upper line with its apex on the
/// lower one. Two cells share an edge in this geometry **exactly** when they are neighbours in
/// <see cref="Lattice.InSlots"/> / <see cref="Lattice.OutSlots"/> — the drawing and the graph are
/// the same object seen twice.
/// </summary>
public sealed class LatticeGeometry
{
    public LatticeGeometry(double side = 1.0)
    {
        if (side <= 0) throw new ArgumentOutOfRangeException(nameof(side), "Side must be positive.");
        Side = side;
        BandHeight = side * Math.Sqrt(3) / 2;
    }

    /// <summary>Edge length of one triangle.</summary>
    public double Side { get; }

    /// <summary>Vertical size of one row-band. Not <see cref="CellCoord.Height"/>, which is a
    /// flow half-level, not a distance.</summary>
    public double BandHeight { get; }

    /// <summary>How far one column step moves horizontally.</summary>
    public double ColStep => Side / 2;

    /// <summary>
    /// The triangle's three corners, counter-clockwise in lattice space (y up).
    /// ▲: base-left, base-right, apex. ▽: apex, base-right, base-left.
    /// </summary>
    public LatticePoint[] Corners(CellCoord coord)
    {
        double left = coord.Col * ColStep;
        double bottom = coord.Row * BandHeight;
        double top = bottom + BandHeight;
        double mid = left + ColStep;

        return coord.IsUp
            ? new[]
            {
                new LatticePoint(left, bottom),
                new LatticePoint(left + Side, bottom),
                new LatticePoint(mid, top),
            }
            : new[]
            {
                new LatticePoint(mid, bottom),
                new LatticePoint(left + Side, top),
                new LatticePoint(left, top),
            };
    }

    /// <summary>
    /// The triangle's centroid — where a crystal glyph sits. Both orientations centre on the same
    /// x; a ▲ sits a third of a band up and a ▽ two thirds, which is exactly why
    /// <see cref="CellCoord.Height"/> orders them the way it does.
    /// </summary>
    public LatticePoint Center(CellCoord coord) => new(
        X: coord.Col * ColStep + ColStep,
        Y: coord.Row * BandHeight + BandHeight * (coord.IsUp ? 1.0 / 3 : 2.0 / 3));

    /// <summary>
    /// Which cell a point falls in — the hit test behind every click. Every point belongs to
    /// exactly one cell, so this always answers; whether that cell is usable is
    /// <see cref="LatticeMask"/>'s question, and whether it holds a crystal is
    /// <see cref="Lattice"/>'s.
    /// </summary>
    public CellCoord Locate(LatticePoint point)
    {
        int row = (int)Math.Floor(point.Y / BandHeight);
        int half = (int)Math.Floor(point.X / ColStep);

        // Position inside that half-column, both in 0..1.
        double t = point.X / ColStep - half;
        double v = point.Y / BandHeight - row;

        // A half-column is split by one diagonal into a ▲ part and a ▽ part. Which diagonal —
        // rising or falling — depends on the half-column's own parity.
        bool rising = ((row + half) & 1) == 0;
        return rising
            ? (v < t ? new CellCoord(row, half) : new CellCoord(row, half - 1))
            : (v < 1 - t ? new CellCoord(row, half - 1) : new CellCoord(row, half));
    }

    /// <summary>
    /// The bounding box of a set of cells in lattice space, as (min corner, max corner) — what a
    /// renderer needs to fit an arbitrarily-shaped mask into a viewport. Empty input gives a
    /// degenerate box at the origin.
    /// </summary>
    public (LatticePoint Min, LatticePoint Max) Extent(IEnumerable<CellCoord> coords)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        foreach (CellCoord coord in coords)
        foreach (LatticePoint corner in Corners(coord))
        {
            any = true;
            if (corner.X < minX) minX = corner.X;
            if (corner.X > maxX) maxX = corner.X;
            if (corner.Y < minY) minY = corner.Y;
            if (corner.Y > maxY) maxY = corner.Y;
        }

        return any
            ? (new LatticePoint(minX, minY), new LatticePoint(maxX, maxY))
            : (new LatticePoint(0, 0), new LatticePoint(0, 0));
    }
}
