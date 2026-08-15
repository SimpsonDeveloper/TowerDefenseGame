using System;
using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Pure topology — no compiling. Everything the compiler relies on being true of the graph
/// before it starts routing.
/// </summary>
public class LatticeTests
{
    [Fact]
    public void OrientationIsTheSlots_NotAToggle()
    {
        // bipartite by construction: parity of (row + col) fixes split/merge arity
        Assert.Equal(Orientation.Up, new CellCoord(2, 0).Orient);
        Assert.Equal(Orientation.Down, new CellCoord(2, 1).Orient);

        foreach (CellCoord slot in Lattice.OutSlots(new CellCoord(2, 0)))
            Assert.Equal(Orientation.Down, slot.Orient);
        foreach (CellCoord slot in Lattice.InSlots(new CellCoord(2, 1)))
            Assert.Equal(Orientation.Up, slot.Orient);
    }

    [Fact]
    public void RowGrowsUpward_WithTheFlow()
    {
        // ▲ pulls from the row BELOW, ▽ pushes to the row ABOVE — so a taller lattice never
        // needs negative rows.
        Assert.Equal(new CellCoord(1, 0), Lattice.InSlots(new CellCoord(2, 0)).Single());
        Assert.Equal(new CellCoord(3, 1), Lattice.OutSlots(new CellCoord(2, 1)).Single());
    }

    [Fact]
    public void Height_SeparatesTheTwoOrientationsInsideABand()
    {
        // a ▲ stands on its band's lower line, the ▽s beside it hang from the upper one
        Assert.True(new CellCoord(0, 0).Height < new CellCoord(0, 1).Height);   // ▲(0) < ▽(0)
        Assert.True(new CellCoord(0, 1).Height < new CellCoord(1, 1).Height);   // ▽(0) < ▲(1)
    }

    [Fact]
    public void FlowOrder_PutsEveryInNeighbourBeforeItsCell()
    {
        // THE precondition of the routing sweep: it reads energy[up] while computing `cell`,
        // so every in-neighbour must already be done. Height strictly increases along every
        // edge, which is what makes this hold.
        Lattice lat = new Lattice();
        lat.Place(0, 2, CrystalKind.Ruby);       // ▲ source
        lat.Place(0, 1, CrystalKind.Sapphire);   // ▽
        lat.Place(0, 3, CrystalKind.Sapphire);   // ▽
        lat.Place(1, 1, CrystalKind.Emerald);    // ▲
        lat.Place(1, 3, CrystalKind.Emerald);    // ▲
        lat.Place(1, 2, CrystalKind.Sapphire);   // ▽ merge

        int[] position = new int[lat.Cells.Count];
        IReadOnlyList<Cell> order = lat.FlowOrder();
        for (int i = 0; i < order.Count; i++) position[order[i].Id] = i;

        foreach (Cell cell in lat.Cells)
        foreach (Cell up in lat.InNeighbors(cell))
            Assert.True(position[up.Id] < position[cell.Id],
                $"{up} must be routed before {cell}");
    }

    [Fact]
    public void FlowOrder_IsBottomToTop_UpBeforeDownInARow()
    {
        // a ▲ feeds the ▽s in its OWN row, so it must be processed first
        Lattice lat = new Lattice();
        Cell up = lat.Place(0, 0, CrystalKind.Ruby);
        Cell down = lat.Place(0, 1, CrystalKind.Sapphire);
        Cell above = lat.Place(1, 1, CrystalKind.Emerald);

        int[] order = lat.FlowOrder().Select(cell => cell.Id).ToArray();

        Assert.Equal(new[] { up.Id, down.Id, above.Id }, order);
    }

    [Fact]
    public void TerminalsArePredicates_NotState()
    {
        // source ⟺ nothing feeds it; sink ⟺ it feeds nothing. Nothing to set, nothing to toggle.
        Lattice lat = new Lattice();
        Cell a = lat.Place(0, 0, CrystalKind.Ruby);       // leaf input
        Cell b = lat.Place(0, 1, CrystalKind.Sapphire);   // interior
        Cell c = lat.Place(1, 1, CrystalKind.Emerald);    // leaf output

        Assert.True(lat.IsSource(a));
        Assert.False(lat.IsSink(a));      // a feeds b
        Assert.False(lat.IsSource(b));
        Assert.False(lat.IsSink(b));
        Assert.False(lat.IsSource(c));    // c is fed by b
        Assert.True(lat.IsSink(c));

        Assert.Equal(new[] { a }, lat.Sources());
        Assert.Equal(new[] { c }, lat.Sinks());
    }

    [Fact]
    public void LoneCrystal_IsBothSourceAndSink()
    {
        Lattice lat = new Lattice();
        Cell lone = lat.Place(0, 0, CrystalKind.Ruby);

        Assert.True(lat.IsSource(lone));
        Assert.True(lat.IsSink(lone));
    }

    [Fact]
    public void PlacingTwice_Throws()
    {
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);

        Assert.Throws<InvalidOperationException>(() => lat.Place(0, 0, CrystalKind.Quartz));
    }
}
