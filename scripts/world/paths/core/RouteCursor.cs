using System.Collections.Generic;

namespace towerdefensegame.scripts.world.paths.core;

/// <summary>Where one enemy is with respect to the route.</summary>
public enum RouteStage
{
    /// <summary>No route laid, so nothing to believe. Heads for the core and breaks off for turrets.</summary>
    Free,

    /// <summary>Walking toward <see cref="RouteCursor{T}.Target"/>, a ward on the route.</summary>
    Following,

    /// <summary>
    /// Past the last ward and on the final leg into the core. Still on the route — the core is its
    /// point 0 — so still railroaded. Wards laid after this do not pull it back out.
    /// </summary>
    Home,
}

/// <summary>
/// One enemy's place on a route: which ward it is walking toward, and what happens to that when
/// the player edits the route under its feet. Design: <c>impl-planning/combat/paths.md</c> §8.
///
/// The route is listed <b>core-outward</b>, in placement order, and walked <b>backwards</b>:
/// <list type="bullet">
/// <item><b>Joining</b> is always at the far end, the last ward placed. That is the entrance;
///   an enemy that joined at the nearest ward instead would skip whatever maze lay behind it.</item>
/// <item><b>Appending</b> a ward never moves anyone, because appending never shifts an index.</item>
/// <item><b>Lifting</b> the ward an enemy is walking toward sends it on to the next surviving ward
///   inward. The route rejoins over the gap, so the enemy does too.</item>
/// <item><b>Lifting every ward</b> leaves no route, and the enemy is free.</item>
/// </list>
///
/// Tracks its target by <b>identity</b>, not index — a removal shifts every index above it, so an
/// index alone would silently retarget the wrong ward. Engine-free and generic over the ward type
/// so it compiles into the test project; the game uses <c>Vector2I</c> tiles.
/// </summary>
public sealed class RouteCursor<T>
{
    /// <summary>The route as last seen, core-outward. Kept so a lifted target can be walked past.</summary>
    private readonly List<T> _seen = new();

    public RouteStage Stage { get; private set; } = RouteStage.Free;

    /// <summary>Index into the route of the ward being walked toward. -1 unless <see cref="Stage"/> is Following.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>The ward being walked toward. Meaningless unless <see cref="Stage"/> is Following.</summary>
    public T Target => Index >= 0 ? _seen[Index] : default;

    /// <summary>True while the enemy believes the route: following it, or on its last leg home.</summary>
    public bool OnRoute => Stage != RouteStage.Free;

    /// <summary>
    /// Bring the cursor up to date with the route. Call on spawn and after every edit.
    /// Returns true when the destination changed — a new ward, or a new stage.
    /// </summary>
    public bool Sync(IReadOnlyList<T> route)
    {
        RouteStage stage = Stage;
        T target = Target;

        if (route.Count == 0)
        {
            Stage = RouteStage.Free;
            Index = -1;
        }
        else if (Stage == RouteStage.Free)
        {
            Stage = RouteStage.Following;
            Index = route.Count - 1;
        }
        else if (Stage == RouteStage.Following)
        {
            Index = Resume(route);
            if (Index < 0) Stage = RouteStage.Home;
        }

        _seen.Clear();
        _seen.AddRange(route);

        // A shifted index with the same ward is not a new destination — the enemy keeps walking.
        return stage != Stage
            || (Stage == RouteStage.Following && !EqualityComparer<T>.Default.Equals(target, Target));
    }

    /// <summary>
    /// The target reached (or found unreachable): move one ward inward, or onto the last leg home.
    /// Returns true when there was somewhere to advance from.
    /// </summary>
    public bool Advance()
    {
        if (Stage != RouteStage.Following) return false;

        Index--;
        if (Index < 0) Stage = RouteStage.Home;
        return true;
    }

    /// <summary>
    /// Where the current target sits in the new route — or, if it was lifted, the first ward inward
    /// of it that survived. -1 when nothing inward survived, which means home.
    /// </summary>
    private int Resume(IReadOnlyList<T> route)
    {
        for (int i = Index; i >= 0; i--)
        {
            int found = IndexOf(route, _seen[i]);
            if (found >= 0) return found;
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<T> route, T ward)
    {
        for (int i = 0; i < route.Count; i++)
            if (EqualityComparer<T>.Default.Equals(route[i], ward)) return i;
        return -1;
    }
}
