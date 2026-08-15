using System;
using System.Collections.Generic;
using System.Linq;

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
/// grows rightward and alternates orientation, so the grid is bipartite by construction.
///
/// A row is one horizontal BAND of the tiling and holds BOTH orientations, interlocked side by
/// side: a ▲ stands on the band's lower line, a ▽ hangs from its upper line. So within a band a
/// ▲ sits below the ▽s beside it — that half-level is what <see cref="FlowDepth"/> encodes.
/// </summary>
public readonly record struct CellCoord(int Row, int Col)
{
    /// <summary>Orientation is the slot's, not a toggle: parity of (row + col).</summary>
    public Orientation Orient => ((Row + Col) & 1) == 0 ? Orientation.Up : Orientation.Down;

    public bool IsUp => Orient == Orientation.Up;

    /// <summary>
    /// Verticality key. Increases with height, so ASCENDING is the bottom→top sweep and
    /// "lowest gem first". Within one row a ▲ sits below the ▽s it feeds, so it ranks lower:
    /// ▲(r) &lt; ▽(r) &lt; ▲(r+1). Also the first-order key for the ordered shot
    /// (op-flow.md, roadmap item 2).
    /// </summary>
    public int FlowDepth => 2 * Row + (IsUp ? 0 : 1);
}

/// <summary>One placed crystal.</summary>
public sealed class Cell
{
    public int Id { get; init; }
    public CellCoord Coord { get; init; }
    public CrystalKind Kind { get; init; }

    public int Row => Coord.Row;
    public int Col => Coord.Col;
    public Orientation Orient => Coord.Orient;
    public bool IsUp => Coord.IsUp;
    public int FlowDepth => Coord.FlowDepth;

    public override string ToString() => $"#{Id} {Kind} {(IsUp ? "▲" : "▽")}({Row},{Col})";
}

/// <summary>
/// The DAG input: which slots hold crystals. Edges, edge roles (in / out) and terminals are all
/// DERIVED from coordinates — terminals are never set by the caller (compiler-core.md §2).
///
/// Adjacency (flow runs upward = toward larger rows):
///   ▲(r,c)  IN  = (r-1, c)                 OUT = (r, c-1), (r, c+1)
///   ▽(r,c)  IN  = (r, c-1), (r, c+1)       OUT = (r+1, c)
/// One cell's OUT edge is always the neighbor's IN edge, and every neighbor is the opposite
/// orientation — the bipartite / split-merge arity rules hold by construction.
/// </summary>
public sealed class Lattice
{
    private readonly Dictionary<CellCoord, Cell> _byCoord = new();
    private readonly List<Cell> _cells = new();

    public IReadOnlyList<Cell> Cells => _cells;
    public int Count => _cells.Count;

    /// <summary>Place a crystal. Returns its id. Throws if the slot is taken.</summary>
    public Cell Place(int row, int col, CrystalKind kind)
    {
        CellCoord coord = new CellCoord(row, col);
        if (_byCoord.ContainsKey(coord))
            throw new InvalidOperationException($"Cell ({row},{col}) already holds a crystal.");

        Cell cell = new Cell { Id = _cells.Count, Coord = coord, Kind = kind };
        _byCoord[coord] = cell;
        _cells.Add(cell);
        return cell;
    }

    public Cell At(int row, int col) => _byCoord.TryGetValue(new CellCoord(row, col), out Cell c) ? c : null;

    /// <summary>Coordinates of the slots feeding this cell, placed or not.</summary>
    public static IEnumerable<CellCoord> InSlots(CellCoord c) => c.IsUp
        ? new[] { new CellCoord(c.Row - 1, c.Col) }
        : new[] { new CellCoord(c.Row, c.Col - 1), new CellCoord(c.Row, c.Col + 1) };

    /// <summary>Coordinates of the slots this cell feeds, placed or not.</summary>
    public static IEnumerable<CellCoord> OutSlots(CellCoord c) => c.IsUp
        ? new[] { new CellCoord(c.Row, c.Col - 1), new CellCoord(c.Row, c.Col + 1) }
        : new[] { new CellCoord(c.Row + 1, c.Col) };

    /// <summary>Placed crystals feeding this cell (its internal in-edges).</summary>
    public IEnumerable<Cell> InNeighbors(Cell cell) => Neighbors(InSlots(cell.Coord));

    /// <summary>Placed crystals this cell feeds (its internal out-edges).</summary>
    public IEnumerable<Cell> OutNeighbors(Cell cell) => Neighbors(OutSlots(cell.Coord));

    private IEnumerable<Cell> Neighbors(IEnumerable<CellCoord> slots)
    {
        foreach (CellCoord s in slots)
            if (_byCoord.TryGetValue(s, out Cell n))
                yield return n;
    }

    /// <summary>True if any in-side slot is empty / off-mask — i.e. the cell has an open input.</summary>
    public bool HasOpenIn(Cell cell) => InSlots(cell.Coord).Any(s => !_byCoord.ContainsKey(s));

    /// <summary>True if any out-side slot is empty / off-mask.</summary>
    public bool HasOpenOut(Cell cell) => OutSlots(cell.Coord).Any(s => !_byCoord.ContainsKey(s));

    /// <summary>
    /// Bottom→top topological order: FlowDepth ascending, then leftmost first. Correct because
    /// a ▲ only ever feeds ▽s in its own row (next FlowDepth up) and a ▽ only feeds the ▲ above.
    /// </summary>
    public IReadOnlyList<Cell> FlowOrder() => _cells
        .OrderBy(c => c.FlowDepth)
        .ThenBy(c => c.Col)
        .ToList();

    /// <summary>Canonical unordered edge key for a placed pair (mirrors the playground's ek()).</summary>
    public static (int, int) EdgeKey(Cell a, Cell b) =>
        a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
}
