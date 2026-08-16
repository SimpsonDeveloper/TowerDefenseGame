using System;
using System.Collections.Generic;
using System.Linq;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// A point in **view space**: pixels, origin top-left, **y grows DOWNWARD**. The opposite of
/// <see cref="LatticePoint"/> in every respect, which is why it is a separate type — the two are
/// never assignable to each other by accident.
/// </summary>
public readonly record struct ViewPoint(double X, double Y)
{
    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}

/// <summary>
/// Fits a lattice into a rectangle and converts between the two coordinate spaces — the whole of
/// "where do I draw this" and "what did the player just click". Engine-free on purpose: this is
/// the part of the UI most likely to disagree with itself, so it is the part that gets tested.
///
/// The renderer keeps only the Godot <c>Vector2</c> conversion.
/// </summary>
public sealed class LatticeCamera
{
    private readonly LatticeGeometry _geo;
    private LatticePoint _min;

    public LatticeCamera(LatticeGeometry geo) =>
        _geo = geo ?? throw new ArgumentNullException(nameof(geo));

    /// <summary>Pixels per unit of lattice space.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Where the framed region's bottom-left corner landed in view space.</summary>
    public ViewPoint Origin { get; private set; }

    /// <summary>
    /// Scale and centre <paramref name="slots"/> inside a <paramref name="viewWidth"/> ×
    /// <paramref name="viewHeight"/> rectangle, keeping <paramref name="margin"/> pixels clear
    /// and the aspect ratio square. A degenerate framing (nothing to show, or no room to show it)
    /// falls back to 1:1 centred rather than dividing by zero.
    /// </summary>
    public void Frame(IEnumerable<CellCoord> slots, double viewWidth, double viewHeight, double margin = 0)
    {
        (LatticePoint min, LatticePoint max) = _geo.Extent(slots);
        _min = min;

        double width = max.X - min.X;
        double height = max.Y - min.Y;

        if (width <= 0 || height <= 0 || viewWidth <= 0 || viewHeight <= 0)
        {
            Scale = 1;
            Origin = new ViewPoint(viewWidth / 2, viewHeight / 2);
            return;
        }

        Scale = Math.Max(1e-6, Math.Min(
            (viewWidth - 2 * margin) / width,
            (viewHeight - 2 * margin) / height));

        double drawnWidth = width * Scale;
        double drawnHeight = height * Scale;
        Origin = new ViewPoint(
            X: (viewWidth - drawnWidth) / 2,
            Y: (viewHeight - drawnHeight) / 2 + drawnHeight);   // the lattice's BOTTOM edge
    }

    /// <summary>Lattice space → view space. The single y flip in the whole UI.</summary>
    public ViewPoint ToView(LatticePoint point) => new(
        X: Origin.X + (point.X - _min.X) * Scale,
        Y: Origin.Y - (point.Y - _min.Y) * Scale);

    /// <summary>View space → lattice space. Exact inverse of <see cref="ToView"/>.</summary>
    public LatticePoint ToLattice(ViewPoint point) => new(
        X: _min.X + (point.X - Origin.X) / Scale,
        Y: _min.Y + (Origin.Y - point.Y) / Scale);

    public ViewPoint CenterOf(CellCoord coord) => ToView(_geo.Center(coord));

    public ViewPoint[] CornersOf(CellCoord coord) => _geo.Corners(coord).Select(ToView).ToArray();

    /// <summary>The whole click path: a pixel the player pressed → the cell under it.</summary>
    public CellCoord Locate(ViewPoint point) => _geo.Locate(ToLattice(point));
}
