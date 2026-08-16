using System;
using System.Collections.Generic;
using System.Linq;
using towerdefensegame.scripts.towers.crystal.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// The click path and the draw path, which are the same path run backwards. Everything the
/// renderer does with coordinates happens here, where it can be checked without an engine.
/// </summary>
public class LatticeCameraTests
{
    private const double Eps = 1e-6;
    private const double Width = 800;
    private const double Height = 480;
    private const double Margin = 24;

    private static LatticeCamera Framed(IEnumerable<CellCoord> slots, double margin = Margin)
    {
        LatticeCamera camera = new LatticeCamera(new LatticeGeometry(side: 1));
        camera.Frame(slots, Width, Height, margin);
        return camera;
    }

    private static IReadOnlyList<CellCoord> Board => LatticeMask.Filled(4, 7).Slots.ToList();

    [Fact]
    public void ClickingACellsPixel_FindsThatCell()
    {
        // THE interaction: press a pixel, get the cell. Runs the fit, the flip and the hit test
        // end to end.
        LatticeCamera camera = Framed(Board);

        foreach (CellCoord coord in Board)
            Assert.Equal(coord, camera.Locate(camera.CenterOf(coord)));
    }

    [Fact]
    public void ViewAndLatticeSpace_AreExactInverses()
    {
        LatticeCamera camera = Framed(Board);

        foreach (CellCoord coord in Board)
        {
            LatticePoint original = new LatticePoint(coord.Col * 0.5, coord.Row * 0.37);
            LatticePoint round = camera.ToLattice(camera.ToView(original));

            Assert.Equal(original.X, round.X, Eps);
            Assert.Equal(original.Y, round.Y, Eps);
        }
    }

    [Fact]
    public void YIsFlippedExactlyOnce()
    {
        // lattice y grows upward with the flow; screen y grows down. A cell higher in the flow
        // must draw NEARER THE TOP.
        LatticeCamera camera = Framed(Board);

        Assert.True(camera.CenterOf(new CellCoord(3, 1)).Y < camera.CenterOf(new CellCoord(0, 1)).Y);
        Assert.True(camera.CenterOf(new CellCoord(0, 1)).Y < camera.CenterOf(new CellCoord(0, 0)).Y);
    }

    [Fact]
    public void FramedLattice_FitsInsideTheMargin()
    {
        LatticeCamera camera = Framed(Board);
        IEnumerable<ViewPoint> corners = Board.SelectMany(camera.CornersOf);

        Assert.All(corners, corner =>
        {
            Assert.InRange(corner.X, Margin - Eps, Width - Margin + Eps);
            Assert.InRange(corner.Y, Margin - Eps, Height - Margin + Eps);
        });
    }

    [Fact]
    public void FramedLattice_IsCentred()
    {
        LatticeCamera camera = Framed(Board);
        IReadOnlyList<ViewPoint> corners = Board.SelectMany(camera.CornersOf).ToList();

        double left = corners.Min(corner => corner.X);
        double right = corners.Max(corner => corner.X);
        double top = corners.Min(corner => corner.Y);
        double bottom = corners.Max(corner => corner.Y);

        Assert.Equal(left, Width - right, 6);
        Assert.Equal(top, Height - bottom, 6);
    }

    [Fact]
    public void FramingIsAspectPreserving_NotStretched()
    {
        // one scale for both axes, so triangles stay equilateral in a non-square viewport
        LatticeCamera wide = Framed(Board);
        CellCoord coord = new CellCoord(1, 2);

        ViewPoint[] corners = wide.CornersOf(coord);
        double sideA = Distance(corners[0], corners[1]);
        double sideB = Distance(corners[1], corners[2]);
        double sideC = Distance(corners[2], corners[0]);

        Assert.Equal(sideA, sideB, 6);
        Assert.Equal(sideB, sideC, 6);
        Assert.Equal(wide.Scale, sideA, 6);   // a unit side scales to exactly Scale pixels
    }

    [Fact]
    public void ATallerViewport_ScalesToWidth()
    {
        LatticeCamera camera = new LatticeCamera(new LatticeGeometry(1));
        camera.Frame(Board, viewWidth: 100, viewHeight: 10_000, margin: 0);

        IEnumerable<ViewPoint> corners = Board.SelectMany(camera.CornersOf);
        Assert.All(corners, corner => Assert.InRange(corner.X, -Eps, 100 + Eps));
    }

    [Fact]
    public void EmptyOrDegenerateFraming_DoesNotDivideByZero()
    {
        LatticeCamera empty = Framed(Array.Empty<CellCoord>());
        Assert.Equal(1, empty.Scale);
        Assert.Equal(new ViewPoint(Width / 2, Height / 2), empty.Origin);

        LatticeCamera noRoom = new LatticeCamera(new LatticeGeometry(1));
        noRoom.Frame(Board, viewWidth: 0, viewHeight: 0, margin: 40);
        Assert.True(noRoom.Scale > 0);
    }

    [Fact]
    public void ClicksSurviveAResize()
    {
        // the view re-frames on every Resized; the cell under a given cell's pixel must not drift
        LatticeCamera camera = new LatticeCamera(new LatticeGeometry(1));

        foreach ((double w, double h) in new[] { (800.0, 480.0), (320.0, 900.0), (1600.0, 200.0) })
        {
            camera.Frame(Board, w, h, margin: 8);
            foreach (CellCoord coord in Board)
                Assert.Equal(coord, camera.Locate(camera.CenterOf(coord)));
        }
    }

    private static double Distance(ViewPoint a, ViewPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
