using System.Collections.Generic;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>The smallest coordinate box containing a set of cells — what a renderer frames.</summary>
public readonly record struct CellBounds(int MinRow, int MaxRow, int MinCol, int MaxCol)
{
    public int Rows => MaxRow - MinRow + 1;
    public int Cols => MaxCol - MinCol + 1;

    public override string ToString() => $"rows {MinRow}..{MaxRow}, cols {MinCol}..{MaxCol}";
}

/// <summary>
/// Which slots of the infinite triangular grid a lattice may ever use — its **shape**, separate
/// from what is placed in it. The real lattice is not a filled rectangle (`lattice-ui.md` §1):
/// slots can be permanently blocked, the perimeter is a contour rather than a box, and the
/// usable region GROWS as the player buys cells.
///
/// Deliberately dumb: a set of coordinates plus <see cref="Allow"/> / <see cref="Block"/>. It
/// knows nothing about crystals, energy or adjacency — the template editor paints it, and
/// <see cref="Lattice"/> enforces it so an off-mask crystal is not expressible.
/// </summary>
public sealed class LatticeMask
{
    private readonly HashSet<CellCoord> _usable = new();

    /// <summary>Every usable slot, whether or not a crystal sits there. Unordered.</summary>
    public IReadOnlyCollection<CellCoord> Slots => _usable;

    public int Count => _usable.Count;

    public bool IsUsable(CellCoord coord) => _usable.Contains(coord);

    public bool IsUsable(int row, int col) => IsUsable(new CellCoord(row, col));

    /// <summary>Open a slot — painting the mask in the editor, or buying a cell in play.</summary>
    public LatticeMask Allow(int row, int col)
    {
        _usable.Add(new CellCoord(row, col));
        return this;
    }

    /// <summary>Close a slot. Does not touch any crystal already placed there.</summary>
    public LatticeMask Block(int row, int col)
    {
        _usable.Remove(new CellCoord(row, col));
        return this;
    }

    /// <summary>
    /// The coordinate box around the usable region, or <c>null</c> if the mask is empty. Note
    /// this is a box in <c>(row, col)</c> — the region inside it can be any contour at all.
    /// </summary>
    public CellBounds? Bounds
    {
        get
        {
            if (_usable.Count == 0) return null;

            int minRow = int.MaxValue, maxRow = int.MinValue;
            int minCol = int.MaxValue, maxCol = int.MinValue;
            foreach (CellCoord coord in _usable)
            {
                if (coord.Row < minRow) minRow = coord.Row;
                if (coord.Row > maxRow) maxRow = coord.Row;
                if (coord.Col < minCol) minCol = coord.Col;
                if (coord.Col > maxCol) maxCol = coord.Col;
            }
            return new CellBounds(minRow, maxRow, minCol, maxCol);
        }
    }

    /// <summary>
    /// The playground's shape: every slot in <c>rows × cols</c> from the origin. A starting point
    /// for authoring, not the shape anything ships with.
    /// </summary>
    public static LatticeMask Filled(int rows, int cols)
    {
        LatticeMask mask = new LatticeMask();
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
            mask.Allow(row, col);
        return mask;
    }

    public override string ToString() => $"mask[{_usable.Count} slots, {Bounds?.ToString() ?? "empty"}]";
}