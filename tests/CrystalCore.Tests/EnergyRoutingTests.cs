using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks the port to docs/tower-design/energy-conservation.md — the local-toll model.
/// Layouts use the lattice adjacency rules directly (row grows UPWARD, with the flow):
///   ▲(r,c) IN = (r-1,c), OUT = (r,c±1)   ▽(r,c) IN = (r,c±1), OUT = (r+1,c)
/// </summary>
public class EnergyRoutingTests
{
    private const double Eps = 1e-9;

    // costs 1 / 2 / 3 from the worked example
    private static readonly ICostTable Costs123 = new FixedCosts(
        (CrystalKind.Ruby, 1), (CrystalKind.Sapphire, 2), (CrystalKind.Emerald, 3));

    [Fact]
    public void Chain_TollsLocally_AndDeliversExitEnergy()
    {
        // a → b → c, costs 1 / 2 / 3, E_core = 20 → in 20 / 19 / 17, exit 14
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 0, CrystalKind.Ruby);       // ▲ leaf input  → source
        Cell b = lat.Place(0, 1, CrystalKind.Sapphire);   // ▽ interior
        Cell c = lat.Place(1, 1, CrystalKind.Emerald);    // ▲ leaf output → sink

        CompileResult r = Compiler.Compile(lat, 20, Costs123);
        Dictionary<int, NodeTrace> trace = r.Trace.ToDictionary(t => t.CellId);

        Assert.Equal(20, trace[a.Id].InSum, Eps);
        Assert.Equal(19, trace[a.Id].OutE, Eps);
        Assert.Equal(19, trace[b.Id].InSum, Eps);
        Assert.Equal(17, trace[b.Id].OutE, Eps);
        Assert.Equal(17, trace[c.Id].InSum, Eps);
        Assert.Equal(14, trace[c.Id].OutE, Eps);

        Assert.Equal(14, r.WeaponEnergy, Eps);
        Assert.Equal(6, r.UsedCost, Eps);
        Assert.False(r.Over);
    }

    [Fact]
    public void Chain_ComboMultiplierIsEnergyArrivingDownstream()
    {
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 0, CrystalKind.Ruby);
        Cell b = lat.Place(0, 1, CrystalKind.Sapphire);
        Cell c = lat.Place(1, 1, CrystalKind.Emerald);

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        EdgeOp ab = Assert.Single(r.EdgeOps, e => e.UpCellId == a.Id && e.DownCellId == b.Id);
        EdgeOp bc = Assert.Single(r.EdgeOps, e => e.UpCellId == b.Id && e.DownCellId == c.Id);

        Assert.Equal(OpId.Frostburn, ab.Op);   // Ruby + Sapphire
        Assert.Equal(19, ab.Energy, Eps);      // energy entering b
        Assert.Equal(OpId.Weather, bc.Op);     // Sapphire + Emerald
        Assert.Equal(17, bc.Energy, Eps);      // energy entering c
        Assert.All(r.EdgeOps, e => Assert.False(e.Debt));
    }

    [Fact]
    public void Split_DividesPostTollEnergy()
    {
        // a → {b, c}: a outputs 19, halved to 9.5 each, then tolled to 7.5 / 6.5
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 2, CrystalKind.Ruby);       // ▲ split, cost 1
        Cell b = lat.Place(0, 1, CrystalKind.Sapphire);   // ▽ sink, cost 2
        Cell c = lat.Place(0, 3, CrystalKind.Emerald);    // ▽ sink, cost 3

        CompileResult r = Compiler.Compile(lat, 20, Costs123);
        Dictionary<int, NodeTrace> trace = r.Trace.ToDictionary(t => t.CellId);

        Assert.Equal(19, trace[a.Id].OutE, Eps);
        Assert.Equal(2, trace[a.Id].OutCount);
        Assert.Equal(9.5, trace[a.Id].PerOut, Eps);
        Assert.Equal(9.5, trace[b.Id].InSum, Eps);
        Assert.Equal(7.5, trace[b.Id].OutE, Eps);
        Assert.Equal(9.5, trace[c.Id].InSum, Eps);
        Assert.Equal(6.5, trace[c.Id].OutE, Eps);

        Assert.Equal(14, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void Debt_FlowsUnclamped_AndAMergeRecoversIt()
    {
        // two sources at 10 each: the left branch tolls into debt (−2), the right carries 8;
        // the ▽ sums them back above 0.
        ICostTable costs = new FixedCosts(
            (CrystalKind.Ruby, 12), (CrystalKind.Emerald, 2), (CrystalKind.Sapphire, 1));

        Lattice lat = new Lattice();
        Cell left = lat.Place(0, 0, CrystalKind.Ruby);      // ▲ source, cost 12
        Cell right = lat.Place(0, 2, CrystalKind.Emerald);  // ▲ source, cost 2
        Cell merge = lat.Place(0, 1, CrystalKind.Sapphire); // ▽ sink,   cost 1

        CompileResult r = Compiler.Compile(lat, 20, costs);
        Dictionary<int, NodeTrace> trace = r.Trace.ToDictionary(t => t.CellId);

        Assert.Equal(-2, trace[left.Id].OutE, Eps);        // debt, not clamped
        Assert.Equal(8, trace[right.Id].OutE, Eps);
        Assert.Equal(6, trace[merge.Id].InSum, Eps);       // −2 + 8 recovered
        Assert.Equal(5, trace[merge.Id].OutE, Eps);
        Assert.Equal(5, r.WeaponEnergy, Eps);

        EdgeOp debted = Assert.Single(r.EdgeOps, e => e.UpCellId == left.Id);
        Assert.True(debted.Debt);
        Assert.Equal(0, debted.Energy, Eps);               // inert op, floored at 0

        EdgeOp live = Assert.Single(r.EdgeOps, e => e.UpCellId == right.Id);
        Assert.False(live.Debt);
        Assert.Equal(8, live.Energy, Eps);
    }

    [Fact]
    public void EnergyIsConserved_NothingCanLeakOutOfTheLattice()
    {
        // The invariant that replaces the old `lostEnergy` counter: every crystal's cost is the
        // ONLY thing that removes energy, so the weapon always collects exactly
        // E_core − Σ cost. Energy cannot dead-end, because following out-edges from any crystal
        // must end at a leaf output and every leaf output IS automatically a sink.
        Lattice lat = new Lattice();
        lat.Place(0, 2, CrystalKind.Ruby);       // ▲ source, cost 1
        lat.Place(0, 3, CrystalKind.Sapphire);   // ▽        cost 2
        lat.Place(1, 3, CrystalKind.Emerald);    // ▲ splits, cost 3
        lat.Place(1, 2, CrystalKind.Sapphire);   // ▽ sink,   cost 2
        lat.Place(1, 4, CrystalKind.Sapphire);   // ▽ sink,   cost 2

        CompileResult r = Compiler.Compile(lat, 40, Costs123);

        Assert.All(r.Trace, t => Assert.True(t.Productive));   // no branch can dead-end
        Assert.Equal(2, r.Sinks.Count);
        Assert.Equal(30, r.WeaponEnergy, Eps);   // 40 −1 → 39 −2 → 37 −3 = 34, /2 = 17, −2 each
        Assert.Equal(r.CoreEnergy - r.UsedCost, r.WeaponEnergy, Eps);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(100)]
    public void EnergyIsConserved_AcrossChainSplitAndMerge(double core)
    {
        // one lattice exercising a source, a split and a merge at once: weapon energy is still
        // exactly E_core − Σ cost, whatever the routing shape.
        Lattice lat = new Lattice();
        lat.Place(0, 2, CrystalKind.Ruby);       // ▲ source, cost 1
        lat.Place(0, 1, CrystalKind.Sapphire);   // ▽ cost 2  ┐ split
        lat.Place(0, 3, CrystalKind.Sapphire);   // ▽ cost 2  ┘
        lat.Place(1, 1, CrystalKind.Emerald);    // ▲ cost 3
        lat.Place(1, 3, CrystalKind.Emerald);    // ▲ cost 3
        lat.Place(1, 2, CrystalKind.Sapphire);   // ▽ cost 2  ← merges both back, sink

        CompileResult r = Compiler.Compile(lat, core, Costs123);

        Assert.Equal(13, r.UsedCost, Eps);
        Assert.Equal(core - 13, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void OverBudget_IsFlagged()
    {
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);      // shipping cost 28
        lat.Place(0, 1, CrystalKind.Sapphire);  // shipping cost 16

        CompileResult r = Compiler.Compile(lat, 20);      // default cost table

        Assert.Equal(44, r.UsedCost, Eps);
        Assert.True(r.Over);
    }
}
