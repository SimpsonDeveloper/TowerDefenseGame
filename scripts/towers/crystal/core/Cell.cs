namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>▲ Up = split (1 in → 2 out, divides). ▽ Down = merge (2 in → 1 out, sums).</summary>
public enum Orientation
{
    Up,
    Down,
}

/// <summary>
/// A slot on the triangular lattice. <c>Row</c> grows UPWARD (row 0 is the bottom), matching the
/// direction energy flows — so growing a lattice taller never needs negative rows. <c>Col</c>
/// grows rightward.
///
/// A row is one horizontal BAND of the tiling and holds BOTH orientations, interlocked side by
/// side: a ▲ stands on the band's lower line, a ▽ hangs from its upper line. So within a band a
/// ▲ sits below the ▽s beside it — that half-level is what <see cref="Height"/> encodes.
/// </summary>
public readonly record struct CellCoord(int Row, int Col)
{
    /// <summary>Orientation is the slot's, not a toggle: parity of (row + col).</summary>
    public Orientation Orient => ((Row + Col) & 1) == 0 ? Orientation.Up : Orientation.Down;

    public bool IsUp => Orient == Orientation.Up;

    /// <summary>
    /// How high this slot sits, in half-levels. Rises with height, and separates the two
    /// orientations inside a band: ▲(r) &lt; ▽(r) &lt; ▲(r+1). Sorting by it ascending is both
    /// the bottom→top routing sweep and the "lowest gem first" key for the ordered shot
    /// (op-flow.md, roadmap item 2).
    /// </summary>
    public int Height => 2 * Row + (IsUp ? 0 : 1);
}

/// <summary>One placed crystal. Immutable; the <see cref="Lattice"/> owns it.</summary>
public sealed class Cell
{
    public Cell(int id, CellCoord coord, CrystalKind kind)
    {
        Id = id;
        Coord = coord;
        Kind = kind;
    }

    public int Id { get; }
    public CellCoord Coord { get; }
    public CrystalKind Kind { get; }

    public int Row => Coord.Row;
    public int Col => Coord.Col;
    public Orientation Orient => Coord.Orient;
    public bool IsUp => Coord.IsUp;
    public int Height => Coord.Height;

    public override string ToString() => $"#{Id} {Kind} {(IsUp ? "▲" : "▽")}({Row},{Col})";
}
