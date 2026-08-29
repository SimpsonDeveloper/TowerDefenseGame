using System;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Burn's numbers, gathered so tuning is a data edit. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/burn.md</c> — that file is the source, this record is the port.
/// </summary>
/// <param name="StacksPerEnergy">Stacks per unit of edge energy. The one place the compiler's
///   continuous energy becomes a discrete pile. Calibrated against a 600-energy core: a bare
///   Ruby pair delivers ~570, which at 1/20 is ~29 stacks.</param>
/// <param name="DamagePerStack">HP per stack, per tick — so the first tick is the biggest and
///   every one after it is smaller.</param>
/// <param name="DecayFraction">Fraction of the standing stacks burnt off each tick, on top of
///   <see cref="BurnTuning.DecayBase"/>. This is what holds the burn's duration roughly flat
///   however big the pile is.</param>
/// <param name="DecayBase">Stacks burnt off each tick regardless of size. Without it the tail
///   would never reach zero — a fraction of 3 truncates to 0.</param>
/// <param name="TickInterval">Seconds between ticks.</param>
public sealed record BurnTuning(
    double StacksPerEnergy = 1.0 / 20.0,
    double DamagePerStack = 1.0,
    double DecayFraction = 0.2,
    int DecayBase = 1,
    double TickInterval = 1.0);

/// <summary>
/// <b>Burn</b> (Ruby · Ruby) — stacks that sizzle down, per
/// <c>effect-vocab/ops/primitives/burn.md</c>.
///
/// Damage per tick is linear in the stacks standing at that moment, and each tick burns off a
/// flat base plus a fraction of the pile. So a burn hits hardest the instant it lands and fades
/// as it consumes itself, and — because the decay is proportional — a huge pile does not burn
/// proportionally longer: it burns proportionally <i>harder</i>. Twice the stacks is roughly
/// twice the total damage in only a few more ticks.
///
/// <b>No cap and no timer.</b> Stacks sum without limit and the burn ends when it has eaten
/// itself, not when a clock runs out. That makes stack count a pure measure of the lattice that
/// produced it.
///
/// Standalone — it consumes nothing. Flareup (Quartz · Ruby) is what spends this pile, and
/// Frostburn converts it; both read it at hit time through <see cref="EnemyState.TakeStacks"/>.
/// </summary>
public sealed class Burn : IOp, ITickingState
{
    private readonly BurnTuning _tuning;

    public Burn(BurnTuning tuning = null) => _tuning = tuning ?? new BurnTuning();

    public OpId Id => OpId.Burn;

    public StateId State => StateId.Burn;

    public double Interval => _tuning.TickInterval;

    /// <summary>
    /// Turn the shot's energy into stacks and add them. Rounded <b>up</b>: any energy at all is
    /// worth a stack, because a weak Ruby pair doing literally nothing reads as a bug rather than
    /// as weakness. Merging with an existing burn is a plain sum, in
    /// <see cref="EnemyState.AddStacks"/>.
    /// </summary>
    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        target.AddStacks(StateId.Burn, (int)Math.Ceiling(quantity * _tuning.StacksPerEnergy));
    }

    /// <summary>
    /// Damage for what is standing, then burn that much of it off. Taking the last stack ends the
    /// state, which is how the burn stops — see <see cref="EnemyState.TakeStacks"/>.
    /// </summary>
    public void Tick(EnemyState enemy)
    {
        int stacks = enemy.Stacks(StateId.Burn);
        if (stacks <= 0) return;

        enemy.DealHpDamage(stacks * _tuning.DamagePerStack);
        enemy.TakeStacks(StateId.Burn, _tuning.DecayBase + (int)(stacks * _tuning.DecayFraction));
    }
}
