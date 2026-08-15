using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Auto source/sink terminals + equal energy split (compiler-core.md §2–§3).
/// The user never sets a terminal; the compiler derives them from geometry every run.
/// </summary>
public class TerminalTests
{
    private const double Eps = 1e-9;

    private static readonly ICostTable Costs123 = new FixedCosts(
        (CrystalKind.Ruby, 1), (CrystalKind.Sapphire, 2), (CrystalKind.Emerald, 3));

    [Fact]
    public void Chain_OnlyLeafInputIsSource_OnlyLeafOutputIsSink()
    {
        var lat = new Lattice();
        var a = lat.Place(0, 0, CrystalKind.Ruby);       // leaf input
        var b = lat.Place(0, 1, CrystalKind.Sapphire);   // interior
        var c = lat.Place(1, 1, CrystalKind.Emerald);    // leaf output

        var r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { a.Id }, r.Sources);
        Assert.Equal(new[] { c.Id }, r.Sinks);

        // a feeds a crystal → NOT a sink; c is fed by a crystal → NOT a source; b is neither.
        Assert.DoesNotContain(a.Id, r.Sinks);
        Assert.DoesNotContain(c.Id, r.Sources);
        Assert.DoesNotContain(b.Id, r.Sources);
        Assert.DoesNotContain(b.Id, r.Sinks);
    }

    [Fact]
    public void LoneCrystal_IsBothSourceAndSink()
    {
        var lat = new Lattice();
        var lone = lat.Place(0, 0, CrystalKind.Ruby);

        var r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { lone.Id }, r.Sources);
        Assert.Equal(new[] { lone.Id }, r.Sinks);

        var t = Assert.Single(r.Terminals);
        Assert.True(t.IsSource);
        Assert.True(t.IsSink);
        Assert.Equal("S1", t.SourceLabel);
        Assert.Equal("T1", t.SinkLabel);
        Assert.Equal(19, r.WeaponEnergy, Eps);   // seeded 20, tolls 1
    }

    [Fact]
    public void TwoSources_SplitCoreEnergyEqually_NoWeights()
    {
        // two non-adjacent lone crystals, E_core = 20 → 10 each
        var lat = new Lattice();
        var one = lat.Place(0, 0, CrystalKind.Ruby);       // cost 1
        var two = lat.Place(0, 4, CrystalKind.Emerald);    // cost 3

        var r = Compiler.Compile(lat, 20, Costs123);
        var byCell = r.Terminals.ToDictionary(t => t.CellId);

        Assert.Equal(2, r.Sources.Count);
        Assert.Equal(10, byCell[one.Id].SourceEnergy, Eps);
        Assert.Equal(10, byCell[two.Id].SourceEnergy, Eps);
        Assert.Equal(9, byCell[one.Id].SinkEnergy, Eps);   // 10 − 1
        Assert.Equal(7, byCell[two.Id].SinkEnergy, Eps);   // 10 − 3
        Assert.Equal(16, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void MergeShape_HasTwoSources_OneSink()
    {
        var lat = new Lattice();
        var left = lat.Place(0, 0, CrystalKind.Ruby);
        var right = lat.Place(0, 2, CrystalKind.Emerald);
        var merge = lat.Place(0, 1, CrystalKind.Sapphire);

        var r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { left.Id, right.Id }, r.Sources);   // ordered leftmost first
        Assert.Equal(new[] { merge.Id }, r.Sinks);

        var labels = r.Terminals.Where(t => t.IsSource)
            .OrderBy(t => t.SourceLabel).Select(t => t.SourceLabel);
        Assert.Equal(new[] { "S1", "S2" }, labels);
    }

    [Fact]
    public void OrientationIsTheSlots_NotAToggle()
    {
        // bipartite by construction: parity of (row + col) fixes split/merge arity
        Assert.Equal(Orientation.Up, new CellCoord(2, 0).Orient);
        Assert.Equal(Orientation.Down, new CellCoord(2, 1).Orient);

        foreach (var slot in Lattice.OutSlots(new CellCoord(2, 0)))
            Assert.Equal(Orientation.Down, slot.Orient);
        foreach (var slot in Lattice.InSlots(new CellCoord(2, 1)))
            Assert.Equal(Orientation.Up, slot.Orient);
    }

    [Fact]
    public void RowGrowsUpward_WithTheFlow()
    {
        // ▲ pulls from the row BELOW, ▽ pushes to the row ABOVE — so a taller lattice never
        // needs negative rows.
        Assert.Equal(new CellCoord(1, 0), Lattice.InSlots(new CellCoord(2, 0)).Single());
        Assert.Equal(new CellCoord(3, 1), Lattice.OutSlots(new CellCoord(2, 1)).Single());

        // and FlowDepth increases with height: ▲(r) < ▽(r) < ▲(r+1)
        Assert.True(new CellCoord(0, 0).FlowDepth < new CellCoord(0, 1).FlowDepth);
        Assert.True(new CellCoord(0, 1).FlowDepth < new CellCoord(1, 1).FlowDepth);
    }

    [Fact]
    public void FlowOrder_IsBottomToTop_UpBeforeDownInARow()
    {
        // a ▲ feeds the ▽s in its OWN row, so it must be processed first
        var lat = new Lattice();
        var up = lat.Place(0, 0, CrystalKind.Ruby);
        var down = lat.Place(0, 1, CrystalKind.Sapphire);
        var above = lat.Place(1, 1, CrystalKind.Emerald);

        var order = lat.FlowOrder().Select(c => c.Id).ToArray();

        Assert.Equal(new[] { up.Id, down.Id, above.Id }, order);
    }
}
