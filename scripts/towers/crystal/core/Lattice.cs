using System;
using System.Collections.Generic;
using System.Linq;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// The compiler's whole input: which slots hold crystals. Everything else about the graph —
/// orientation, adjacency, which crystals are terminals, what order to walk them in — is a
/// question you ask the lattice, never a value the caller supplies.
///
/// Adjacency (flow runs upward = toward larger rows):
///   ▲(r,c)  IN  = (r-1, c)                 OUT = (r, c-1), (r, c+1)
///   ▽(r,c)  IN  = (r, c-1), (r, c+1)       OUT = (r+1, c)
/// One cell's OUT slot is always its neighbour's IN slot, and every neighbour is the opposite
/// orientation, so the bipartite rule and the ▲ 1→2 / ▽ 2→1 arities hold by construction.
/// </summary>
public sealed class Lattice
{
    private readonly Dictionary<CellCoord, Cell> _byCoord = new();
    private readonly List<Cell> _cells = new();

    public IReadOnlyList<Cell> Cells => _cells;

    /// <summary>Place a crystal. Throws if the slot is already taken.</summary>
    public Cell Place(int row, int col, CrystalKind kind)
    {
        CellCoord coord = new CellCoord(row, col);
        if (_byCoord.ContainsKey(coord))
            throw new InvalidOperationException($"Cell ({row},{col}) already holds a crystal.");

        Cell cell = new Cell(_cells.Count, coord, kind);
        _byCoord[coord] = cell;
        _cells.Add(cell);
        return cell;
    }

    public Cell At(int row, int col) =>
        _byCoord.TryGetValue(new CellCoord(row, col), out Cell cell) ? cell : null;

    // ---- topology -------------------------------------------------------------------------

    /// <summary>Slots that feed this one, whether or not a crystal sits there.</summary>
    public static IEnumerable<CellCoord> InSlots(CellCoord c) => c.IsUp
        ? new[] { new CellCoord(c.Row - 1, c.Col) }
        : new[] { new CellCoord(c.Row, c.Col - 1), new CellCoord(c.Row, c.Col + 1) };

    /// <summary>Slots this one feeds, whether or not a crystal sits there.</summary>
    public static IEnumerable<CellCoord> OutSlots(CellCoord c) => c.IsUp
        ? new[] { new CellCoord(c.Row, c.Col - 1), new CellCoord(c.Row, c.Col + 1) }
        : new[] { new CellCoord(c.Row + 1, c.Col) };

    /// <summary>Crystals handing energy up to this one.</summary>
    public IReadOnlyList<Cell> InNeighbors(Cell cell) => Placed(InSlots(cell.Coord));

    /// <summary>Crystals this one hands energy up to.</summary>
    public IReadOnlyList<Cell> OutNeighbors(Cell cell) => Placed(OutSlots(cell.Coord));

    private List<Cell> Placed(IEnumerable<CellCoord> slots)
    {
        List<Cell> found = new(2);
        foreach (CellCoord slot in slots)
            if (_byCoord.TryGetValue(slot, out Cell cell))
                found.Add(cell);
        return found;
    }

    // ---- terminals (derived, never set) ---------------------------------------------------

    /// <summary>
    /// A crystal is a SOURCE exactly when nothing feeds it — it is a leaf on its input side, so
    /// the core seeds it directly. Not a flag and not a user choice: it is this predicate.
    /// </summary>
    public bool IsSource(Cell cell) => InNeighbors(cell).Count == 0;

    /// <summary>
    /// A crystal is a SINK exactly when it feeds nothing — a leaf on its output side, so its
    /// post-toll energy drains to the weapon. A lone crystal is both source and sink.
    /// </summary>
    public bool IsSink(Cell cell) => OutNeighbors(cell).Count == 0;

    public IReadOnlyList<Cell> Sources() => FlowOrder().Where(IsSource).ToList();

    public IReadOnlyList<Cell> Sinks() => FlowOrder().Where(IsSink).ToList();

    // ---- walk order -----------------------------------------------------------------------

    /// <summary>
    /// Bottom→top, and the only order the routing sweep is valid in: it guarantees every
    /// crystal's in-neighbours come before it. True because a ▲ feeds only the ▽s in its own
    /// row (one half-level up) and a ▽ feeds only the ▲ in the row above (also one half-level
    /// up) — so <see cref="CellCoord.Height"/> strictly increases along every edge.
    /// Ties (same height) are broken leftmost-first for determinism.
    /// </summary>
    public IReadOnlyList<Cell> FlowOrder() => _cells
        .OrderBy(cell => cell.Height)
        .ThenBy(cell => cell.Col)
        .ToList();
}
