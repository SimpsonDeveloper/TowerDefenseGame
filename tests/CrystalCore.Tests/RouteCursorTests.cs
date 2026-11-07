using System.Collections.Generic;
using towerdefensegame.scripts.world.paths.core;
using Xunit;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Locks the route follower to docs/tower-design/impl-planning/combat/paths.md §8: join at the far
/// end, walk inward, and survive the player editing the route mid-walk. Wards are ints here; the
/// game uses tiles. Routes are listed core-outward, as <c>MiragePath.Wards</c> lists them.
/// </summary>
public class RouteCursorTests
{
    private static RouteCursor<int> Joined(params int[] route)
    {
        RouteCursor<int> cursor = new();
        cursor.Sync(route);
        return cursor;
    }

    [Fact]
    public void NoRouteMeansFree()
    {
        RouteCursor<int> cursor = new();

        Assert.False(cursor.Sync(new List<int>()));
        Assert.Equal(RouteStage.Free, cursor.Stage);
        Assert.False(cursor.OnRoute);
    }

    [Fact]
    public void JoinsAtTheFarEnd()
    {
        RouteCursor<int> cursor = new();

        Assert.True(cursor.Sync(new[] { 10, 20, 30 }));
        Assert.Equal(RouteStage.Following, cursor.Stage);
        Assert.Equal(30, cursor.Target);
        Assert.True(cursor.OnRoute);
    }

    [Fact]
    public void WalksInwardThenHome()
    {
        RouteCursor<int> cursor = Joined(10, 20, 30);

        Assert.True(cursor.Advance());
        Assert.Equal(20, cursor.Target);
        Assert.True(cursor.Advance());
        Assert.Equal(10, cursor.Target);
        Assert.True(cursor.Advance());

        Assert.Equal(RouteStage.Home, cursor.Stage);
        Assert.True(cursor.OnRoute);   // the core is point 0: the last leg is still the route
        Assert.False(cursor.Advance());
    }

    [Fact]
    public void AppendingDoesNotMoveAWalker()
    {
        RouteCursor<int> cursor = Joined(10, 20, 30);
        cursor.Advance();   // on its way to 20

        Assert.False(cursor.Sync(new[] { 10, 20, 30, 40 }));
        Assert.Equal(20, cursor.Target);
    }

    [Fact]
    public void LiftingTheTargetMovesOnToTheNextWardInward()
    {
        RouteCursor<int> cursor = Joined(10, 20, 30);
        cursor.Advance();   // on its way to 20

        Assert.True(cursor.Sync(new[] { 10, 30 }));
        Assert.Equal(10, cursor.Target);
    }

    [Fact]
    public void LiftingAWardBehindKeepsTheTargetDespiteTheShiftedIndex()
    {
        RouteCursor<int> cursor = Joined(10, 20, 30);   // on its way to 30, index 2

        Assert.False(cursor.Sync(new[] { 20, 30 }));
        Assert.Equal(30, cursor.Target);
        Assert.Equal(1, cursor.Index);
    }

    [Fact]
    public void LiftingEverythingInwardSendsItHome()
    {
        RouteCursor<int> cursor = Joined(10, 20, 30);
        cursor.Advance();   // on its way to 20

        Assert.True(cursor.Sync(new[] { 30 }));
        Assert.Equal(RouteStage.Home, cursor.Stage);
    }

    [Fact]
    public void ClearingTheRouteFreesIt()
    {
        RouteCursor<int> cursor = Joined(10, 20);

        Assert.True(cursor.Sync(new List<int>()));
        Assert.Equal(RouteStage.Free, cursor.Stage);
    }

    [Fact]
    public void HomeIgnoresNewWards()
    {
        RouteCursor<int> cursor = Joined(10);
        cursor.Advance();

        Assert.False(cursor.Sync(new[] { 10, 20 }));
        Assert.Equal(RouteStage.Home, cursor.Stage);
    }

    [Fact]
    public void AFreedEnemyRejoinsAtTheFarEndOfANewRoute()
    {
        RouteCursor<int> cursor = Joined(10);
        cursor.Sync(new List<int>());

        Assert.True(cursor.Sync(new[] { 50, 60 }));
        Assert.Equal(60, cursor.Target);
    }
}
