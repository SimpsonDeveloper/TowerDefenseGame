using System;
using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Where cells actually sit (`lattice-ui.md` §2). The load-bearing claim is that the drawing and
/// the graph are the same object: two triangles share an edge on screen exactly when they are
/// in/out neighbours in <see cref="Lattice"/>.
/// </summary>
public class LatticeGeometryTests
{
    private const double Eps = 1e-9;

    private static readonly LatticeGeometry Geo = new LatticeGeometry(side: 1);

    private static IEnumerable<CellCoord> Patch =>
        from row in Enumerable.Range(-2, 5)
        from col in Enumerable.Range(-4, 9)
        select new CellCoord(row, col);

    [Fact]
    public void AdjacentCells_ShareAnEdge_AndNonAdjacentOnesDoNot()
    {
        // THE invariant: geometry and topology are one scheme. A shared edge is two shared
        // corners; anything that is not a flow neighbour shares at most a single point.
        foreach (CellCoord coord in Patch)
        {
            IEnumerable<CellCoord> neighbours = Lattice.InSlots(coord).Concat(Lattice.OutSlots(coord));
            HashSet<CellCoord> expected = neighbours.ToHashSet();

            foreach (CellCoord other in Patch)
            {
                if (other == coord) continue;
                int shared = Geo.Corners(coord).Count(a => Geo.Corners(other).Any(b => Near(a, b)));

                if (expected.Contains(other))
                    Assert.True(shared == 2, $"{coord} and neighbour {other} share {shared} corners");
                else
                    Assert.True(shared <= 1, $"{coord} and non-neighbour {other} share {shared} corners");
            }
        }
    }

    [Fact]
    public void Locate_RoundTripsEveryCellCentre()
    {
        // the click path: pixel → coord must invert coord → pixel, negatives included
        foreach (CellCoord coord in Patch)
            Assert.Equal(coord, Geo.Locate(Geo.Center(coord)));
    }

    [Fact]
    public void Locate_AgreesWithTheTriangleNearItsCorners()
    {
        // sample just inside each corner, where a wrong diagonal would show up first
        foreach (CellCoord coord in Patch)
        {
            LatticePoint centre = Geo.Center(coord);
            foreach (LatticePoint corner in Geo.Corners(coord))
            {
                LatticePoint inside = new LatticePoint(
                    corner.X + (centre.X - corner.X) * 0.02,
                    corner.Y + (centre.Y - corner.Y) * 0.02);
                Assert.Equal(coord, Geo.Locate(inside));
            }
        }
    }

    [Fact]
    public void CentreHeightOrder_MatchesFlowHeight()
    {
        // the ▲ in a band really does sit BELOW the ▽s beside it — the half-level that
        // CellCoord.Height encodes is a physical fact about the tiling, not a convention
        List<CellCoord> byFlow = Patch.OrderBy(c => c.Height).ThenBy(c => c.Col).ToList();

        foreach ((CellCoord lower, CellCoord higher) in byFlow.Zip(byFlow.Skip(1)))
        {
            if (lower.Height == higher.Height)
                Assert.Equal(Geo.Center(lower).Y, Geo.Center(higher).Y, Eps);
            else
                Assert.True(Geo.Center(lower).Y < Geo.Center(higher).Y,
                    $"{lower} (Height {lower.Height}) should sit below {higher} (Height {higher.Height})");
        }
    }

    [Fact]
    public void FlowRunsUpward_AnOutNeighbourIsNeverLower()
    {
        foreach (CellCoord coord in Patch)
        foreach (CellCoord downstream in Lattice.OutSlots(coord))
            Assert.True(Geo.Center(downstream).Y > Geo.Center(coord).Y);
    }

    [Fact]
    public void Side_ScalesEverythingLinearly()
    {
        LatticeGeometry big = new LatticeGeometry(side: 32);
        CellCoord coord = new CellCoord(2, 3);

        Assert.Equal(32 * Math.Sqrt(3) / 2, big.BandHeight, Eps);
        Assert.Equal(Geo.Center(coord).X * 32, big.Center(coord).X, Eps);
        Assert.Equal(Geo.Center(coord).Y * 32, big.Center(coord).Y, Eps);
        Assert.Equal(coord, big.Locate(big.Center(coord)));
    }

    [Fact]
    public void Extent_BoundsAnArbitraryMask()
    {
        // a renderer frames the CONTOUR, not a rectangle — Extent takes whatever cells exist
        LatticeMask mask = new LatticeMask().Allow(0, 0).Allow(0, 1).Allow(1, 1);

        (LatticePoint min, LatticePoint max) = Geo.Extent(mask.Slots);

        Assert.Equal(0, min.X, Eps);                       // ▲(0,0)'s base-left
        Assert.Equal(1.5, max.X, Eps);                     // ▽(0,1)'s base-right at 0.5 + 1
        Assert.Equal(0, min.Y, Eps);
        Assert.Equal(2 * Geo.BandHeight, max.Y, Eps);      // ▲(1,1)'s apex
    }

    [Fact]
    public void Extent_OfNothing_IsDegenerate()
    {
        (LatticePoint min, LatticePoint max) = Geo.Extent(Array.Empty<CellCoord>());

        Assert.Equal(new LatticePoint(0, 0), min);
        Assert.Equal(new LatticePoint(0, 0), max);
    }

    [Fact]
    public void Side_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatticeGeometry(0));
    }

    private static bool Near(LatticePoint a, LatticePoint b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
}
