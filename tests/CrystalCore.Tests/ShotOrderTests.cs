using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks the port to docs/tower-design/impl-planning/upgrades/op-flow.md — the compiled shot.
/// Every op is anchored to its DOWNSTREAM crystal, and the list is sorted
/// `Height` asc → downstream col asc → upstream col asc → op name.
/// </summary>
public class ShotOrderTests
{
    private const double Eps = 1e-9;

    // costs 1 / 2 / 3 from the worked examples
    private static readonly ICostTable Costs123 = new FixedCosts(
        (CrystalKind.Ruby, 1), (CrystalKind.Sapphire, 2), (CrystalKind.Emerald, 3));

    [Fact]
    public void WorkedExample_ChainOrdersLowestGemFirst()
    {
        // op-flow.md §2: Ruby → Ruby → Sapphire produces Burn at the middle gem and
        // Frostburn at the top gem, in that order.
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);       // ▲ source
        lat.Place(0, 1, CrystalKind.Ruby);       // ▽ middle
        lat.Place(1, 1, CrystalKind.Sapphire);   // ▲ top, sink

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { OpId.Burn, OpId.Frostburn }, r.Shot.Select(s => s.Op));
        Assert.Equal(19, r.Shot[0].Quantity, Eps);   // energy arriving at the middle gem
        Assert.Equal(18, r.Shot[1].Quantity, Eps);   // energy arriving at the top gem
    }

    [Fact]
    public void NothingIsConsumedAtCompileTime_PrimitiveAndItsInteractiveBothRide()
    {
        // Frostburn consumes Burn — but on the ENEMY, at hit time. The compiler emits both.
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);
        lat.Place(0, 1, CrystalKind.Ruby);
        lat.Place(1, 1, CrystalKind.Sapphire);

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Contains(r.Shot, s => Ops.IsPrimitive(s.Op));
        Assert.Contains(r.Shot, s => Ops.IsInteractive(s.Op));
        Assert.Equal(r.Ops.Count, r.Shot.Count);     // one entry per edge, none eaten
    }

    [Fact]
    public void VerticalBeatsHorizontal_AHigherGemIsAlwaysLast()
    {
        // Two disjoint chains: a tall one on the left, a short one far to the right. The
        // right-hand op is lower, so it fires BEFORE the left-hand op sitting above it.
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);        // ▲ source ┐ tall chain, cols 0–1
        lat.Place(0, 1, CrystalKind.Ruby);        // ▽        │
        lat.Place(1, 1, CrystalKind.Sapphire);    // ▲ sink   ┘  Frostburn lands at Height 2
        lat.Place(0, 4, CrystalKind.Emerald);     // ▲ source ┐ short chain, cols 4–5
        lat.Place(0, 5, CrystalKind.Emerald);     // ▽ sink   ┘  Corrode lands at Height 1

        CompileResult r = Compiler.Compile(lat, 40, Costs123);

        Assert.Equal(
            new[] { OpId.Burn, OpId.Corrode, OpId.Frostburn },
            r.Shot.Select(s => s.Op));
    }

    [Fact]
    public void SameHeight_OrdersLeftmostDownstreamFirst()
    {
        // A split lands its two ops at the same Height — column decides, NOT the op name
        // (alphabetically Accelerant would come first).
        Lattice lat = new Lattice();
        lat.Place(0, 2, CrystalKind.Ruby);        // ▲ source, cost 1
        lat.Place(0, 1, CrystalKind.Sapphire);    // ▽ sink — Ruby·Sapphire  = Frostburn
        lat.Place(0, 3, CrystalKind.Emerald);     // ▽ sink — Ruby·Emerald   = Accelerant

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { OpId.Frostburn, OpId.Accelerant }, r.Shot.Select(s => s.Op));
        Assert.All(r.Shot, s => Assert.Equal(9.5, s.Quantity, Eps));   // 19 halved by the split
    }

    [Fact]
    public void SameDownstreamGem_OrdersLeftmostUpstreamFirst()
    {
        // Both edges land on the one ▽, so Height and downstream col tie; the upstream column
        // breaks it — again against alphabetical order (Frostburn < Weather).
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Emerald);     // ▲ source — Emerald·Sapphire = Weather
        lat.Place(0, 2, CrystalKind.Ruby);        // ▲ source — Ruby·Sapphire    = Frostburn
        lat.Place(0, 1, CrystalKind.Sapphire);    // ▽ sink, fed by both

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Equal(new[] { OpId.Weather, OpId.Frostburn }, r.Shot.Select(s => s.Op));
        Assert.Equal(7, r.Shot[0].Quantity, Eps);   // 10 − 3
        Assert.Equal(9, r.Shot[1].Quantity, Eps);   // 10 − 1
    }

    [Fact]
    public void DebtedEdge_ProducesNothing()
    {
        // op-flow.md §2: "Debt / zero-energy edges produce nothing." The op still exists as an
        // edge — it just carries no energy, so it never reaches the shot.
        ICostTable costs = new FixedCosts(
            (CrystalKind.Ruby, 12), (CrystalKind.Emerald, 2), (CrystalKind.Sapphire, 1));

        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);        // ▲ source → out −2, in debt
        lat.Place(0, 2, CrystalKind.Emerald);     // ▲ source → out 8
        lat.Place(0, 1, CrystalKind.Sapphire);    // ▽ sink

        CompileResult r = Compiler.Compile(lat, 20, costs);

        Assert.Equal(2, r.Ops.Count);
        ShotOp only = Assert.Single(r.Shot);
        Assert.Equal(OpId.Weather, only.Op);       // Emerald·Sapphire, the solvent branch
        Assert.Equal(8, only.Quantity, Eps);
    }

    [Fact]
    public void LoneCrystal_FiresNoOps()
    {
        // no edges, so no combos — the shot is empty even though energy still reaches the weapon
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);

        CompileResult r = Compiler.Compile(lat, 20, Costs123);

        Assert.Empty(r.Shot);
        Assert.Equal(19, r.WeaponEnergy, Eps);
    }

    [Fact]
    public void SplitAndMergeBack_WalksTheLatticeBottomToTop()
    {
        // Split into two symmetric branches that merge again — six edges across three heights,
        // hand-worked. Note the merge does NOT sum op quantities: AcidArc appears twice.
        Lattice lat = new Lattice();
        lat.Place(0, 2, CrystalKind.Ruby);        // ▲ source, cost 1
        lat.Place(0, 1, CrystalKind.Sapphire);    // ▽ cost 2  ┐ split — Height 1
        lat.Place(0, 3, CrystalKind.Sapphire);    // ▽ cost 2  ┘
        lat.Place(1, 1, CrystalKind.Emerald);     // ▲ cost 3  ┐ Height 2
        lat.Place(1, 3, CrystalKind.Emerald);     // ▲ cost 3  ┘
        lat.Place(1, 2, CrystalKind.Citrine);     // ▽ merge, sink — Height 3

        CompileResult r = Compiler.Compile(lat, 100, Costs123);

        Assert.Equal(
            new[]
            {
                OpId.Frostburn, OpId.Frostburn,   // Ruby·Sapphire  at (0,1) then (0,3)
                OpId.Weather, OpId.Weather,       // Sapphire·Emerald at (1,1) then (1,3)
                OpId.AcidArc, OpId.AcidArc,       // Emerald·Citrine, both onto (1,2)
            },
            r.Shot.Select(s => s.Op));

        Assert.Equal(new[] { 49.5, 49.5, 47.5, 47.5, 44.5, 44.5 }, r.Shot.Select(s => s.Quantity));
    }
}
