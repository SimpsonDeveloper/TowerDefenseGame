using System;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// The lattice's SHAPE (`lattice-ui.md` §1): the usable region is an arbitrary contour that
/// grows, not the playground's filled rectangle. Covers the mask itself and the way
/// <see cref="Lattice"/> enforces it.
/// </summary>
public class LatticeMaskTests
{
    [Fact]
    public void NoMask_MeansTheWholeGrid()
    {
        // what the compiler's own tests use — shape is not a compiler concern
        Lattice lat = new Lattice();

        Assert.Null(lat.Mask);
        Assert.True(lat.CanPlace(-7, 400));
        lat.Place(-7, 400, CrystalKind.Ruby);
    }

    [Fact]
    public void BlockedSlot_IsNotPlaceable()
    {
        LatticeMask mask = new LatticeMask().Allow(0, 0).Allow(0, 1);
        Lattice lat = new Lattice(mask);

        Assert.True(lat.CanPlace(0, 1));
        Assert.False(lat.CanPlace(0, 2));                       // never on the mask
        Assert.Throws<InvalidOperationException>(() => lat.Place(0, 2, CrystalKind.Ruby));
    }

    [Fact]
    public void OccupiedSlot_IsUsableButNotPlaceable()
    {
        // the two rejection reasons are different questions with the same answer
        Lattice lat = new Lattice(new LatticeMask().Allow(0, 0));
        lat.Place(0, 0, CrystalKind.Ruby);

        Assert.True(lat.Mask.IsUsable(0, 0));
        Assert.False(lat.CanPlace(0, 0));
        Assert.Throws<InvalidOperationException>(() => lat.Place(0, 0, CrystalKind.Quartz));
    }

    [Fact]
    public void Allow_GrowsTheUsableRegion()
    {
        // the investment axis: buying a cell is one Allow
        LatticeMask mask = new LatticeMask().Allow(0, 0);
        Lattice lat = new Lattice(mask);

        Assert.False(lat.CanPlace(0, 1));
        mask.Allow(0, 1);
        Assert.True(lat.CanPlace(0, 1));
    }

    [Fact]
    public void Block_TakesOutTheCrystalStandingThere()
    {
        // the whole point of routing shape edits through the lattice: a crystal on a blocked
        // slot is never expressible, so nothing downstream has to check for one
        LatticeMask mask = new LatticeMask().Allow(0, 0).Allow(0, 1);
        Lattice lat = new Lattice(mask);
        lat.Place(0, 0, CrystalKind.Ruby);
        lat.Place(0, 1, CrystalKind.Sapphire);

        Assert.True(lat.Block(0, 0));

        Assert.Null(lat.At(0, 0));
        Assert.Single(lat.Cells);
        Assert.False(mask.IsUsable(0, 0));
        Assert.Empty(LatticeSnapshot.Of(lat).Problems());
    }

    [Fact]
    public void ShapeEdits_ReportWhetherAnythingChanged()
    {
        LatticeMask mask = new LatticeMask().Allow(0, 0);
        Lattice lat = new Lattice(mask);

        Assert.False(lat.Allow(0, 0));   // already open
        Assert.True(lat.Block(0, 0));
        Assert.False(lat.Block(0, 0));   // already closed
        Assert.True(lat.Allow(0, 0));
    }

    [Fact]
    public void ShapeEdits_ThrowWithoutAMask()
    {
        Lattice lat = new Lattice();

        Assert.Throws<InvalidOperationException>(() => lat.Block(0, 0));
        Assert.Throws<InvalidOperationException>(() => lat.Allow(0, 0));
    }

    [Fact]
    public void Bounds_FramesAnUnevenContour()
    {
        LatticeMask mask = new LatticeMask()
            .Allow(0, 0).Allow(0, 1).Allow(0, 2)
            .Allow(1, 1)
            .Allow(2, -3);

        CellBounds? bounds = mask.Bounds;

        Assert.Equal(new CellBounds(MinRow: 0, MaxRow: 2, MinCol: -3, MaxCol: 2), bounds);
        Assert.Equal(3, bounds.Value.Rows);
        Assert.Equal(6, bounds.Value.Cols);   // the BOX is 6 wide; only 5 slots are usable
        Assert.Equal(5, mask.Count);
    }

    [Fact]
    public void EmptyMask_HasNoBounds()
    {
        Assert.Null(new LatticeMask().Bounds);
    }

    [Fact]
    public void Filled_IsThePlaygroundShape()
    {
        LatticeMask mask = LatticeMask.Filled(rows: 3, cols: 4);

        Assert.Equal(12, mask.Count);
        Assert.Equal(new CellBounds(0, 2, 0, 3), mask.Bounds);
        Assert.Equal(6, mask.Slots.Count(slot => slot.IsUp));   // both orientations, interlocked
    }

    [Fact]
    public void Removal_DropsTheEdgesAndKeepsIdsUnique()
    {
        // Cell.Id keys a cached CompileResult.Energy, so a removed id must never come back on a
        // different crystal.
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 0, CrystalKind.Ruby);
        Cell b = lat.Place(0, 1, CrystalKind.Sapphire);

        Assert.Equal(new[] { a }, lat.InNeighbors(b));

        Assert.True(lat.Remove(0, 0));
        Assert.False(lat.Remove(0, 0));                 // already gone
        Assert.Empty(lat.InNeighbors(b));               // b is its own source now
        Assert.True(lat.IsSource(b));

        Cell c = lat.Place(0, 0, CrystalKind.Emerald);
        Assert.NotEqual(a.Id, c.Id);
        Assert.NotEqual(b.Id, c.Id);
    }

    [Fact]
    public void MaskedLattice_CompilesLikeAnyOther()
    {
        // the mask shapes what can be built; it changes nothing about compiling what was
        LatticeMask mask = LatticeMask.Filled(2, 3).Block(0, 2);
        Lattice lat = new Lattice(mask);
        lat.Place(0, 0, CrystalKind.Ruby);
        lat.Place(0, 1, CrystalKind.Ruby);
        lat.Place(1, 1, CrystalKind.Sapphire);

        CompileResult r = Compiler.Compile(lat, 20,
            new FixedCosts((CrystalKind.Ruby, 1), (CrystalKind.Sapphire, 2)));

        Assert.Equal(new[] { OpId.Burn, OpId.Frostburn }, r.Shot.Select(s => s.Op));
        Assert.Equal(16, r.WeaponEnergy, 1e-9);
    }
}
