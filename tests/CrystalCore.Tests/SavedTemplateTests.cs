using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks the on-disk save format to a template actually authored in the editor:
/// <c>resources/crystal_templates/lopsided.tres</c>. The coordinates below are that file's,
/// verbatim.
///
/// The round-trip tests prove a snapshot survives a round trip in memory. This proves the
/// numbers that reached the FILE rebuild the lattice the editor was showing — which is what
/// silently breaks if <see cref="CrystalKind"/> is ever reordered, or if the packing in
/// <c>CrystalTemplate</c> changes axis order.
/// </summary>
public class SavedTemplateTests
{
    private const double Eps = 1e-9;

    // Mask = Array[Vector2i]([...]) — (row, col)
    private static readonly (int Row, int Col)[] MaskSlots =
    {
        (0, -2), (0, -1), (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (0, 6),
        (1, -4), (1, -3), (1, -2), (1, -1), (1, 0), (1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6),
        (2, 1), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6),
        (3, 2), (3, 3), (3, 4), (3, 5), (3, 6),
    };

    // Crystals = Array[Vector3i]([...]) — (row, col, kind index)
    private static readonly (int Row, int Col, int Kind)[] Placements =
    {
        (0, -2, 1), (0, -1, 1), (1, -3, 1), (1, -2, 1),
        (1, -1, 2), (1, 0, 2), (1, 1, 2), (1, 2, 2), (2, 1, 2), (2, 2, 2),
        (2, 3, 5), (3, 2, 5), (3, 3, 5),
    };

    private static LatticeSnapshot Saved() => new LatticeSnapshot(
        MaskSlots.Select(slot => new CellCoord(slot.Row, slot.Col)),
        Placements.Select(p => new PlacedCrystal(p.Row, p.Col, (CrystalKind)p.Kind)));

    [Fact]
    public void TheKindIndicesStillMeanWhatTheyMeantWhenItWasSaved()
    {
        // the one assumption CrystalTemplate makes by packing kind as an int
        Assert.Equal(CrystalKind.Ruby, (CrystalKind)0);
        Assert.Equal(CrystalKind.Sapphire, (CrystalKind)1);
        Assert.Equal(CrystalKind.Emerald, (CrystalKind)2);
        Assert.Equal(CrystalKind.Citrine, (CrystalKind)3);
        Assert.Equal(CrystalKind.Amethyst, (CrystalKind)4);
        Assert.Equal(CrystalKind.Quartz, (CrystalKind)5);
    }

    [Fact]
    public void ItRestoresCleanly_NegativeColumnsAndAll()
    {
        LatticeSnapshot snapshot = Saved();
        Assert.Empty(snapshot.Problems());

        Lattice lattice = snapshot.Restore();

        Assert.Equal(31, lattice.Mask.Count);
        Assert.Equal(13, lattice.Cells.Count);
        Assert.Empty(lattice.OffMask());
        Assert.True(lattice.Mask.IsUsable(1, -4));      // painting left of the origin survives
        Assert.Equal(CrystalKind.Sapphire, lattice.At(1, -3).Kind);
    }

    [Fact]
    public void ItCompilesToWhatTheEditorShowed()
    {
        CompileResult result = Compiler.Compile(Saved().Restore(), 600);

        Assert.Equal(214, result.UsedCost, Eps);        // 4 Sa + 6 Em + 3 Qz
        Assert.Equal(386, result.WeaponEnergy, Eps);
        Assert.Equal(600 - 214, result.WeaponEnergy, Eps);

        Assert.Equal(new[] { "S1", "S2", "S3" }, result.Sources.Select(t => t.Label));
        Assert.All(result.Sources, source => Assert.Equal(200, source.Energy, Eps));
        Assert.Equal(new[] { "T1", "T2", "T3", "T4" }, result.Sinks.Select(t => t.Label));
        Assert.Equal(new[] { 241d, 140d, 0.5d, 4.5d }, result.Sinks.Select(t => t.Energy));
    }

    [Fact]
    public void ItsShotIsTheOneTheEditorListed()
    {
        CompileResult result = Compiler.Compile(Saved().Restore(), 600);

        IReadOnlyList<(OpId, double)> expected = new[]
        {
            (OpId.ChillFreeze, 184d), (OpId.Weather, 168d), (OpId.ChillFreeze, 184d),
            (OpId.Weather, 73d), (OpId.Corrode, 73d),
            (OpId.Corrode, 89d), (OpId.Corrode, 89d),
            (OpId.Corrode, 67d), (OpId.Corrode, 22.5d),
            (OpId.Dissolve, 22.5d), (OpId.Purify, 16.5d), (OpId.Purify, 10.5d),
        };

        Assert.Equal(expected, result.Shot.Select(op => (op.Op, op.Quantity)));
    }
}
