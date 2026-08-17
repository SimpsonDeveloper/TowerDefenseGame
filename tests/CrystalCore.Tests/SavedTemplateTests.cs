using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks the on-disk save format to a template actually authored in the editor, frozen at
/// <c>fixtures/lopsided.tres</c>. The test owns that copy, so the editor's own saves under
/// <c>res://</c> stay disposable.
///
/// The round-trip tests prove a snapshot survives in memory. This reads the real serialized
/// **text** and proves the numbers that reached the file rebuild the lattice the editor was
/// showing — which is what silently breaks if <see cref="CrystalKind"/> is reordered, or if the
/// packing in <c>CrystalTemplate</c> changes axis order.
///
/// Parsing <c>.tres</c> by regex is fine here precisely because the fixture is frozen: it is
/// never rewritten, so there is no format drift to chase.
/// </summary>
public class SavedTemplateTests
{
    private const double Eps = 1e-9;

    private static readonly Lazy<LatticeSnapshot> Fixture = new(LoadFixture);

    private static LatticeSnapshot Saved() => Fixture.Value;

    private static LatticeSnapshot LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "lopsided.tres");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        string mask = Line(path, "Mask");
        string crystals = Line(path, "Crystals");

        IEnumerable<CellCoord> slots = Matches(mask, @"Vector2i\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)")
            .Select(g => new CellCoord(int.Parse(g[1].Value), int.Parse(g[2].Value)));

        IEnumerable<PlacedCrystal> placed = Matches(crystals, @"Vector3i\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)")
            .Select(g => new PlacedCrystal(int.Parse(g[1].Value), int.Parse(g[2].Value), (CrystalKind)int.Parse(g[3].Value)));

        return new LatticeSnapshot(slots, placed);
    }

    private static string Line(string path, string key) =>
        File.ReadLines(path).FirstOrDefault(line => line.StartsWith($"{key} =", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"fixture has no `{key} =` line");

    private static IEnumerable<GroupCollection> Matches(string text, string pattern) =>
        Regex.Matches(text, pattern).Select(match => match.Groups);

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
    public void TheFileParsesToTheShapeThatWasSaved()
    {
        LatticeSnapshot snapshot = Saved();

        Assert.Equal(31, snapshot.Mask.Count);
        Assert.Equal(13, snapshot.Crystals.Count);
        Assert.Empty(snapshot.Problems());

        // the lists are written canonically, so the file is diffable
        Assert.Equal(snapshot.Mask.OrderBy(s => s.Row).ThenBy(s => s.Col), snapshot.Mask);
        Assert.Equal(snapshot.Crystals.OrderBy(c => c.Row).ThenBy(c => c.Col), snapshot.Crystals);
    }

    [Fact]
    public void ItRestoresCleanly_NegativeColumnsAndAll()
    {
        Lattice lattice = Saved().Restore();

        Assert.Equal(31, lattice.Mask.Count);
        Assert.Equal(13, lattice.Cells.Count);
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
