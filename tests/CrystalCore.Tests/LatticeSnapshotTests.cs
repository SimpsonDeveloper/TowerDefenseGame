using System;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// What a template contains and whether it survives a round trip (`lattice-ui.md` §4). The
/// `.tres` shell is Godot's problem; everything that can actually lose data is here.
/// </summary>
public class LatticeSnapshotTests
{
    private static Lattice Board()
    {
        LatticeMask mask = LatticeMask.Filled(3, 5).Block(2, 4);
        Lattice lattice = new Lattice(mask);
        lattice.Place(0, 2, CrystalKind.Ruby);
        lattice.Place(0, 1, CrystalKind.Sapphire);
        lattice.Place(0, 3, CrystalKind.Emerald);
        lattice.Place(1, 1, CrystalKind.Citrine);
        return lattice;
    }

    [Fact]
    public void RoundTrip_PreservesShapeCrystalsAndCompiledResult()
    {
        // the whole point: what you save is what you load, down to the shot
        Lattice original = Board();
        CompileResult before = Compiler.Compile(original, 300);

        Lattice restored = LatticeSnapshot.Of(original).Restore();
        CompileResult after = Compiler.Compile(restored, 300);

        Assert.Equal(original.Mask.Count, restored.Mask.Count);
        Assert.All(original.Mask.Slots, slot => Assert.True(restored.Mask.IsUsable(slot)));

        Assert.Equal(
            original.Cells.Select(cell => (cell.Row, cell.Col, cell.Kind)).OrderBy(c => c.Row).ThenBy(c => c.Col),
            restored.Cells.Select(cell => (cell.Row, cell.Col, cell.Kind)).OrderBy(c => c.Row).ThenBy(c => c.Col));

        Assert.Equal(before.WeaponEnergy, after.WeaponEnergy, 1e-9);
        Assert.Equal(before.Shot.Select(op => op.Op), after.Shot.Select(op => op.Op));
        Assert.Equal(before.Shot.Select(op => op.Quantity), after.Shot.Select(op => op.Quantity));
    }

    [Fact]
    public void BlockedSlots_StayBlockedAcrossTheRoundTrip()
    {
        // the mask is the shape; a hole in it is content, not an accident to be filled in
        Lattice restored = LatticeSnapshot.Of(Board()).Restore();

        Assert.False(restored.Mask.IsUsable(2, 4));
        Assert.False(restored.CanPlace(2, 4));
    }

    [Fact]
    public void ListsAreCanonicallySorted_SoFilesDiffCleanly()
    {
        // two lattices built in different orders must write byte-identical templates
        LatticeMask mask = new LatticeMask().Allow(0, 0).Allow(1, 1).Allow(0, 1);

        Lattice built = new Lattice(mask);
        built.Place(1, 1, CrystalKind.Emerald);
        built.Place(0, 0, CrystalKind.Ruby);

        LatticeSnapshot snapshot = LatticeSnapshot.Of(built);

        Assert.Equal(new[] { new CellCoord(0, 0), new CellCoord(0, 1), new CellCoord(1, 1) }, snapshot.Mask);
        Assert.Equal(
            new[]
            {
                new PlacedCrystal(0, 0, CrystalKind.Ruby),
                new PlacedCrystal(1, 1, CrystalKind.Emerald),
            },
            snapshot.Crystals);
    }

    [Fact]
    public void AnUnmaskedLattice_CannotBecomeATemplate()
    {
        // a template IS a shape — there is nothing to save about an unbounded lattice
        Lattice sprawling = new Lattice();
        sprawling.Place(0, 0, CrystalKind.Ruby);

        Assert.Throws<InvalidOperationException>(() => LatticeSnapshot.Of(sprawling));
    }

    [Fact]
    public void ACrystalOutsideTheMask_IsReportedAndRefused()
    {
        // unbuildable through the editor, but a hand-edited .tres can say it
        LatticeSnapshot broken = new LatticeSnapshot(
            new[] { new CellCoord(0, 0) },
            new[] { new PlacedCrystal(0, 0, CrystalKind.Ruby), new PlacedCrystal(5, 5, CrystalKind.Quartz) });

        Assert.Contains(broken.Problems(), problem => problem.Contains("outside the mask"));
        Assert.Throws<InvalidOperationException>(() => broken.Restore());
    }

    [Fact]
    public void TwoCrystalsInOneSlot_IsReportedAndRefused()
    {
        LatticeSnapshot broken = new LatticeSnapshot(
            new[] { new CellCoord(0, 0) },
            new[] { new PlacedCrystal(0, 0, CrystalKind.Ruby), new PlacedCrystal(0, 0, CrystalKind.Quartz) });

        Assert.Contains(broken.Problems(), problem => problem.Contains("share (0,0)"));
        Assert.Throws<InvalidOperationException>(() => broken.Restore());
    }

    [Fact]
    public void OverBudget_IsNotATemplateProblem()
    {
        // a template does not know which tower loads it, so it cannot know the core energy
        Lattice expensive = new Lattice(LatticeMask.Filled(1, 3));
        expensive.Place(0, 0, CrystalKind.Ruby);
        expensive.Place(0, 1, CrystalKind.Ruby);

        LatticeSnapshot snapshot = LatticeSnapshot.Of(expensive);

        Assert.Empty(snapshot.Problems());
        Assert.True(Compiler.Compile(snapshot.Restore(), 10).Over);
    }

    [Fact]
    public void AnEmptyShape_IsSavableAndRestorable()
    {
        Lattice blank = new Lattice(LatticeMask.Filled(2, 4));

        LatticeSnapshot snapshot = LatticeSnapshot.Of(blank);
        Lattice restored = snapshot.Restore();

        Assert.Empty(snapshot.Crystals);
        Assert.Empty(restored.Cells);
        Assert.Equal(8, restored.Mask.Count);
    }
}
