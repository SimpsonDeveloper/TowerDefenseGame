using System.Collections.Generic;
using towerdefensegame.scripts.combat.core.ops;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// Which behaviour is wired to which op and which state — the whole combat vocabulary in one
/// lookup, so adding a primitive is a registration rather than a branch somewhere.
///
/// <b>A missing entry is a no-op, not an error.</b> That is the load-bearing property: 21 ops are
/// named (<see cref="OpId"/>) and 1 is written, yet a shot carrying any of them resolves
/// end-to-end today. Nothing warns, because "not built yet" is the expected state of this table
/// for the whole of roadmap item 4.
/// </summary>
public sealed class CombatRules
{
    private readonly Dictionary<OpId, IOp> _ops = new();
    private readonly Dictionary<StateId, ITickingState> _tickers = new();

    /// <summary>Everything currently implemented. Tests build their own to isolate one op.</summary>
    public static CombatRules Default { get; } = new CombatRules().Add(new Burn());

    /// <summary>
    /// Register a primitive. Generic rather than two overloads because a primitive usually
    /// implements <b>both</b> faces — Burn is one class that applies stacks and ticks them — and
    /// overloads would make that call ambiguous.
    /// </summary>
    public CombatRules Add<T>(T behavior)
    {
        if (behavior is IOp op) _ops[op.Id] = op;
        if (behavior is ITickingState ticker) _tickers[ticker.State] = ticker;
        return this;
    }

    /// <summary>The handler for an op, or <c>null</c> if it is not written yet.</summary>
    public IOp Op(OpId id) => _ops.TryGetValue(id, out IOp op) ? op : null;

    /// <summary>
    /// Every state that ticks. <see cref="EnemyState"/> walks this rather than its own states,
    /// so a tick that writes a second state cannot invalidate the loop it is running inside.
    /// </summary>
    public IReadOnlyCollection<ITickingState> Tickers => _tickers.Values;
}
