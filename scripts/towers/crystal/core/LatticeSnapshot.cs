using System;
using System.Collections.Generic;
using System.Linq;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>One crystal in a saved lattice: where it sits and what it is.</summary>
public readonly record struct PlacedCrystal(int Row, int Col, CrystalKind Kind)
{
    public CellCoord Coord => new CellCoord(Row, Col);

    public override string ToString() => $"{Kind}({Row},{Col})";
}

/// <summary>
/// A lattice flattened to plain data: **a shape and what starts in it**. The whole content of a
/// template (`lattice-ui.md` §4), and engine-free, so the round trip is unit-tested without a
/// `Resource` in sight — `CrystalTemplate` is only a serialization shell over this.
///
/// Nothing derived is stored. No terminals (they are predicates), no weights (they do not
/// exist), no energy — core energy is a *tower* stat, and a template has no idea which tower
/// will load it.
///
/// Both lists are sorted by <c>(row, col)</c> so the same lattice always writes the same file
/// and a diff shows a real edit rather than a reordering.
/// </summary>
public sealed class LatticeSnapshot
{
    public LatticeSnapshot(IEnumerable<CellCoord> mask, IEnumerable<PlacedCrystal> crystals)
    {
        Mask = (mask ?? throw new ArgumentNullException(nameof(mask)))
            .OrderBy(slot => slot.Row).ThenBy(slot => slot.Col).ToList();
        Crystals = (crystals ?? throw new ArgumentNullException(nameof(crystals)))
            .OrderBy(crystal => crystal.Row).ThenBy(crystal => crystal.Col).ToList();
    }

    /// <summary>The usable slots — the lattice's shape.</summary>
    public IReadOnlyList<CellCoord> Mask { get; }

    /// <summary>What the lattice starts with. A template is a starting point, not a save.</summary>
    public IReadOnlyList<PlacedCrystal> Crystals { get; }

    /// <summary>
    /// Capture a lattice. It must be masked: a template *is* a shape, so there is nothing
    /// meaningful to save about a lattice allowed to sprawl over the infinite grid.
    /// </summary>
    public static LatticeSnapshot Of(Lattice lattice)
    {
        ArgumentNullException.ThrowIfNull(lattice);
        if (lattice.Mask == null)
            throw new InvalidOperationException(
                "Cannot snapshot an unmasked lattice — a template has to have a shape.");

        return new LatticeSnapshot(
            lattice.Mask.Slots,
            lattice.Cells.Select(cell => new PlacedCrystal(cell.Row, cell.Col, cell.Kind)));
    }

    /// <summary>
    /// What a hand-edited file can still get wrong. Auto-terminals already made most illegal
    /// states unbuildable, so this is short: nothing can be in two places, and nothing can stand
    /// outside the shape.
    ///
    /// Over-budget is deliberately **not** here — that is a fact about a lattice paired with a
    /// core energy, not about the template.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = new();
        HashSet<CellCoord> usable = Mask.ToHashSet();

        if (usable.Count != Mask.Count)
            problems.Add($"Mask lists {Mask.Count - usable.Count} duplicate slot(s).");

        HashSet<CellCoord> seen = new();
        foreach (PlacedCrystal crystal in Crystals)
        {
            if (!seen.Add(crystal.Coord))
                problems.Add($"Two crystals share ({crystal.Row},{crystal.Col}).");
            if (!usable.Contains(crystal.Coord))
                problems.Add($"{crystal} stands outside the mask.");
        }

        return problems;
    }

    /// <summary>
    /// Rebuild the lattice. Throws on any <see cref="Problems"/> rather than quietly dropping a
    /// crystal — a template that cannot be restored exactly is a broken file, not a lenient one.
    /// </summary>
    public Lattice Restore()
    {
        IReadOnlyList<string> problems = Problems();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Cannot restore this lattice:" + Environment.NewLine + string.Join(Environment.NewLine, problems));

        LatticeMask mask = new LatticeMask();
        foreach (CellCoord slot in Mask) mask.Allow(slot.Row, slot.Col);

        Lattice lattice = new Lattice(mask);
        foreach (PlacedCrystal crystal in Crystals)
            lattice.Place(crystal.Row, crystal.Col, crystal.Kind);

        return lattice;
    }

    public override string ToString() => $"snapshot[{Mask.Count} slots, {Crystals.Count} crystals]";
}
