using System;
using System.Collections.Generic;

namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// Everything a shot can write on one enemy, and the only thing an op is handed to mutate:
/// stacks, flat states, the R meter, and pending HP damage.
///
/// <b>Engine-free by contract</b> — the same rule the compiler core follows. It never touches a
/// <c>HealthComponent</c>; damage is queued here and taken by the Godot component that owns this
/// object (<see cref="TakeHpDamage"/>). That is what keeps every op's arithmetic testable without
/// a scene tree.
///
/// <b>This class holds state and time. It holds no policy.</b> Stacks sum and that is all — no
/// cap, no lifetime, no decay curve, because those differ per op and belong to the op
/// (<c>effect-vocab/ops/</c>). What lives here is only what every state shares: the numbers
/// themselves, and a tick clock per state.
///
/// The traffic across the boundary is <b>stats in, damage out</b>: <see cref="Vitals"/> is what an
/// op may know about the enemy, and it holds no current HP on purpose — see
/// <see cref="EnemyVitals"/>.
/// </summary>
public sealed class EnemyState
{
    private readonly Dictionary<StateId, int> _stacks = new();
    private readonly Dictionary<StateId, double> _flags = new();

    /// <summary>
    /// Seconds until each ticking state's next tick. Keyed by state, not by op, so a state's
    /// rhythm is its own: it is armed when the state first appears and keeps its phase across
    /// later applications. Two ops ticking at the same interval therefore stay offset by whenever
    /// each one first landed, rather than snapping into lockstep.
    /// </summary>
    private readonly Dictionary<StateId, double> _tickTimers = new();

    private double _pendingHpDamage;

    /// <summary>
    /// What ops may read about the enemy itself. Set once by the owner; reassigning it later does
    /// not re-fill <see cref="R"/>, which is a live meter rather than a stat.
    /// </summary>
    public EnemyVitals Vitals { get; set; }

    /// <summary>
    /// Illusion resistance — the second bar, drained by mind-damage only (<c>illusion.md</c>).
    /// Innate and per-enemy: never spread, never applied, and no op writes it up. Roadmap item 5
    /// gives it behaviour; it lives here now so ops have somewhere to drain to.
    /// </summary>
    public double R { get; private set; }

    public EnemyState(EnemyVitals vitals = null)
    {
        Vitals = vitals ?? new EnemyVitals();
        R = Vitals.MaxR;
    }

    /// <summary>
    /// Raised right after a state's op ticks. Exists for observers that must not influence
    /// anything — the debug readout, and effect visuals later — so it carries the state and
    /// nothing else. A handler that mutates the enemy is doing so mid-tick, and the tick loop
    /// will not have noticed.
    /// </summary>
    public event Action<StateId> Ticked;

    /// <summary>Is this state on the enemy at all — as stacks or as a flag?</summary>
    public bool IsActive(StateId state) => _stacks.ContainsKey(state) || _flags.ContainsKey(state);

    /// <summary>Every state currently carried, in no particular order. For readouts.</summary>
    public IEnumerable<StateId> ActiveStates
    {
        get
        {
            foreach (StateId state in _stacks.Keys) yield return state;
            foreach (StateId state in _flags.Keys)
                if (!_stacks.ContainsKey(state)) yield return state;
        }
    }

    /// <summary>Seconds left on a flat state, 0 for a stack pile or a clockless charge.</summary>
    public double FlagTimeLeft(StateId state) => _flags.TryGetValue(state, out double left) ? left : 0;

    /// <summary>Seconds until this state's next tick, or 0 if nothing ticks it.</summary>
    public double TimeToNextTick(StateId state) => _tickTimers.TryGetValue(state, out double due) ? due : 0;

    // ---- stacks -----------------------------------------------------------------------------

    public int Stacks(StateId state) => _stacks.TryGetValue(state, out int stacks) ? stacks : 0;

    /// <summary>
    /// Add stacks. Merge is a plain <b>sum</b> — two towers burning one enemy add up
    /// (<c>merge.md</c>) — and it is uncapped: how a pile of stacks stops being dangerous is the
    /// op's decay curve, not a ceiling here.
    /// </summary>
    public void AddStacks(StateId state, int amount)
    {
        if (amount <= 0) return;
        _stacks[state] = Stacks(state) + amount;
    }

    /// <summary>
    /// Spend stacks — a consumer at hit time, or a ticking op eating its own. Returns how many it
    /// actually got, which is all it may convert (1-to-1 for now, <c>states.md</c>). Taking the
    /// last one ends the state, and its tick clock with it.
    /// </summary>
    public int TakeStacks(StateId state, int amount)
    {
        int held = Stacks(state);
        if (amount <= 0 || held == 0) return 0;

        int taken = amount < held ? amount : held;
        if (held - taken == 0) Clear(state);
        else _stacks[state] = held - taken;

        return taken;
    }

    // ---- flat states ------------------------------------------------------------------------

    /// <summary>
    /// Raise a flat state for a time. Merge is <b>max</b> — a refresh can only extend
    /// (<c>merge.md</c>). Pass a non-positive duration for a one-shot charge with no clock, like
    /// Brittle, which ends when its consumer eats it rather than when a timer runs out.
    /// </summary>
    public void SetFlag(StateId state, double duration)
    {
        if (duration <= 0)
        {
            if (!_flags.ContainsKey(state)) _flags[state] = 0;
            return;
        }

        _flags[state] = _flags.TryGetValue(state, out double left) && left > duration ? left : duration;
    }

    /// <summary>Strip a state outright — Purify, or a consumer spending a one-shot charge.</summary>
    public void Clear(StateId state)
    {
        _stacks.Remove(state);
        _flags.Remove(state);
        _tickTimers.Remove(state);
    }

    // ---- damage -----------------------------------------------------------------------------

    /// <summary>Queue HP damage. Fractional, and it stays fractional — HP is a double.</summary>
    public void DealHpDamage(double amount)
    {
        if (amount > 0) _pendingHpDamage += amount;
    }

    /// <summary>
    /// Take the damage owed and reset the queue. Queued rather than applied directly because this
    /// class cannot see a <c>HealthComponent</c>; its owner drains it once a frame. Nothing is
    /// rounded or held back.
    /// </summary>
    public double TakeHpDamage()
    {
        double owed = _pendingHpDamage;
        _pendingHpDamage = 0;
        return owed;
    }

    /// <summary>Drain the R meter. Floors at 0; what empty R *means* is item 5.</summary>
    public void DrainR(double amount)
    {
        if (amount <= 0) return;
        R = R - amount < 0 ? 0 : R - amount;
    }

    // ---- time -------------------------------------------------------------------------------

    /// <summary>
    /// Advance every clock: flat timers expire, and every active ticking state fires its op.
    ///
    /// The tick itself is <b>one call</b> — <see cref="ITickingState.Tick"/> — and the op decides
    /// everything behind it: what damage to deal, how many of its own stacks to spend, whether to
    /// end. A state whose op is not registered simply never ticks, the same "missing handler =
    /// no-op" rule the op registry follows.
    /// </summary>
    public void Tick(double delta, CombatRules rules)
    {
        if (delta <= 0) return;

        TickFlags(delta);
        TickStates(delta, rules ?? CombatRules.Default);
    }

    private void TickFlags(double delta)
    {
        // Collected first: rewriting a value mid-enumeration invalidates the enumerator. A flag
        // at 0 has no clock — a one-shot charge, cleared by its consumer — so it is left alone.
        List<StateId> timed = null;

        foreach (KeyValuePair<StateId, double> flag in _flags)
            if (flag.Value > 0)
                (timed ??= new List<StateId>()).Add(flag.Key);

        if (timed == null) return;

        foreach (StateId state in timed)
        {
            double left = _flags[state] - delta;
            if (left > 0) _flags[state] = left;
            else Clear(state);
        }
    }

    /// <summary>
    /// Walks the registered tickers rather than the state dictionaries: a tick is free to write
    /// another state — Frostburn ticks chill — and iterating what it might mutate would be a
    /// live enumerator over a changing map.
    /// </summary>
    private void TickStates(double delta, CombatRules rules)
    {
        foreach (ITickingState ticker in rules.Tickers)
        {
            if (!IsActive(ticker.State)) continue;

            // Armed on first sight, so a fresh state waits a full interval before its first tick
            // instead of firing the instant it lands.
            if (!_tickTimers.TryGetValue(ticker.State, out double due))
                due = ticker.Interval;

            due -= delta;

            // A loop, not an `if`: one long frame owes more than one tick, and dropping the extra
            // would quietly make a lagging game cheaper for the enemy.
            while (due <= 0 && ticker.Interval > 0)
            {
                ticker.Tick(this);
                Ticked?.Invoke(ticker.State);
                if (!IsActive(ticker.State)) break;   // the tick ended its own state
                due += ticker.Interval;
            }

            if (IsActive(ticker.State)) _tickTimers[ticker.State] = due;
        }
    }
}
