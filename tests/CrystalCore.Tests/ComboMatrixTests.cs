using System;
using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

public class ComboMatrixTests
{
    private static readonly CrystalKind[] All = Enum.GetValues<CrystalKind>();

    [Fact]
    public void Matrix_IsSymmetric()
    {
        foreach (CrystalKind a in All)
        foreach (CrystalKind b in All)
            Assert.Equal(ComboMatrix.ComboOp(a, b), ComboMatrix.ComboOp(b, a));
    }

    [Fact]
    public void EveryPair_NamesAnOp()
    {
        foreach (CrystalKind a in All)
        foreach (CrystalKind b in All)
            Assert.NotEqual(OpId.None, ComboMatrix.ComboOp(a, b));
    }

    [Fact]
    public void Diagonal_IsTheNativeOp()
    {
        Assert.Equal(OpId.Burn, ComboMatrix.NativeOp(CrystalKind.Ruby));
        Assert.Equal(OpId.ChillFreeze, ComboMatrix.NativeOp(CrystalKind.Sapphire));
        Assert.Equal(OpId.Corrode, ComboMatrix.NativeOp(CrystalKind.Emerald));
        Assert.Equal(OpId.Scramble, ComboMatrix.NativeOp(CrystalKind.Citrine));
        Assert.Equal(OpId.MindDamage, ComboMatrix.NativeOp(CrystalKind.Amethyst));
        Assert.Equal(OpId.Purify, ComboMatrix.NativeOp(CrystalKind.Quartz));
    }

    [Fact]
    public void SevenPrimitives_FourteenInteractives()
    {
        List<OpId> ops = Enum.GetValues<OpId>().Where(o => o != OpId.None).ToList();

        Assert.Equal(21, ops.Count);
        Assert.Equal(7, ops.Count(Ops.IsPrimitive));
        Assert.Equal(14, ops.Count(Ops.IsInteractive));
    }

    [Fact]
    public void Shot_IsStubbedEmpty_UntilOpFlowLands()
    {
        // roadmap item 1 exposes the seam only; the collect-and-order pass is item 2
        Lattice lat = new Lattice();
        lat.Place(0, 0, CrystalKind.Ruby);
        lat.Place(0, 1, CrystalKind.Ruby);
        lat.Place(1, 1, CrystalKind.Sapphire);

        CompileResult r = Compiler.Compile(lat, 100);

        Assert.NotEmpty(r.Ops);
        Assert.Empty(r.Shot);
    }
}
