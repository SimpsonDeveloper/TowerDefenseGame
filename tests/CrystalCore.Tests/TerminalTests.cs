using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// What terminals look like in a <see cref="CompileResult"/>: labels, seeded shares, drained
/// energy. The topology behind them is covered in <see cref="LatticeTests"/>.
/// </summary>
public class TerminalTests
{
    private const double Eps = 1e-9;

    private static readonly ICostTable Costs123 = new FixedCosts(
        (CrystalKind.Ruby, 1), (CrystalKind.Sapphire, 2), (CrystalKind.Emerald, 3));

    [Fact]
    public void Chain_ReportsOneSourceAndOneSink()
    {
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 0, CrystalKind.Ruby);       // leaf input
        lat.Place(0, 1, CrystalKind.Sapphire);            // interior — neither
        Cell c = lat.Place(1, 1, CrystalKind.Emerald);    // leaf output

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { a }, r.Sources.Select(terminal => terminal.Cell));
        Assert.Equal(new[] { c }, r.Sinks.Select(terminal => terminal.Cell));
        Assert.Equal("S1", r.Sources[0].Label);
        Assert.Equal("T1", r.Sinks[0].Label);
    }

    [Fact]
    public void LoneCrystal_AppearsAsBothSourceAndSink()
    {
        Lattice lat = new Lattice();
        Cell lone = lat.Place(0, 0, CrystalKind.Ruby);

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(lone, Assert.Single(r.Sources).Cell);
        Assert.Equal(lone, Assert.Single(r.Sinks).Cell);
        Assert.Equal(20, r.Sources[0].Energy, Eps);   // seeded the whole core
        Assert.Equal(19, r.Sinks[0].Energy, Eps);     // …minus its own toll of 1
        Assert.Equal(19, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void TwoSources_SplitCoreEnergyEqually_NoWeights()
    {
        // two non-adjacent lone crystals, E_core = 20 → 10 each
        Lattice lat = new Lattice();
        Cell one = lat.Place(0, 0, CrystalKind.Ruby);       // cost 1
        Cell two = lat.Place(0, 4, CrystalKind.Emerald);    // cost 3

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(2, r.Sources.Count);
        Assert.All(r.Sources, source => Assert.Equal(10, source.Energy, Eps));

        Assert.Equal(9, r.Sinks.Single(t => t.Cell == one).Energy, Eps);   // 10 − 1
        Assert.Equal(7, r.Sinks.Single(t => t.Cell == two).Energy, Eps);   // 10 − 3
        Assert.Equal(16, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void MergeShape_LabelsSourcesLeftmostFirst()
    {
        Lattice lat = new Lattice();
        Cell left = lat.Place(0, 0, CrystalKind.Ruby);
        Cell right = lat.Place(0, 2, CrystalKind.Emerald);
        Cell merge = lat.Place(0, 1, CrystalKind.Sapphire);

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { left, right }, r.Sources.Select(terminal => terminal.Cell));
        Assert.Equal(new[] { "S1", "S2" }, r.Sources.Select(terminal => terminal.Label));
        Assert.Equal(merge, Assert.Single(r.Sinks).Cell);
    }

    [Fact]
    public void SinkEnergy_IsFlooredAtZero()
    {
        // one crystal that costs more than the whole core
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);   // shipping cost 28

        CompileResult r = Compiler.Compile(lat, 10);

        Assert.Equal(0, Assert.Single(r.Sinks).Energy, Eps);
        Assert.Equal(0, r.WeaponEnergy, Eps);
        Assert.True(r.Over);
    }
}
