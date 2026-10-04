using System;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Corrode's numbers, gathered so tuning is a data edit. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/corrode.md</c> — that file is the source, this record is the
/// port.
/// </summary>
/// <param name="StacksPerEnergy">Stacks per unit of edge energy, rounded up. Emerald·Emerald off
///   the 150-core starter turret delivers ~128, which at 1/20 is ~7 stacks a shot.</param>
/// <param name="BoutThreshold">Stacks a bout costs to start — the <c>X</c> of the design.</param>
/// <param name="BoutTicks">How many ticks one bout runs for.</param>
/// <param name="DamageFraction">Fraction of the enemy's <b>max</b> HP eaten per tick — the
///   <c>Y</c>. This is what makes acid the answer to a health pool flat damage cannot dent.</param>
/// <param name="TickInterval">Seconds between bout ticks.</param>
public sealed record CorrodeTuning(
    double StacksPerEnergy = 1.0 / 20.0,
    int BoutThreshold = 10,
    int BoutTicks = 10,
    double DamageFraction = 0.02,
    double TickInterval = 0.5);

/// <summary>
/// <b>Corrode</b> (Emerald · Emerald) — acid that pools before it eats, per
/// <c>effect-vocab/ops/primitives/corrode.md</c>.
///
/// Stacks bank harmlessly. At <see cref="CorrodeTuning.BoutThreshold"/> the op spends that many
/// and opens a <b>bout</b>: a fixed run of ticks, each eating a percentage of the enemy's max HP.
/// Overflow stays banked, so sustained fire chains one bout into the next instead of wasting it.
///
/// The opposite shape to Burn on purpose. Burn is immediate and fades; Corrode is delayed and
/// flat. Burn rewards one big hit, Corrode rewards sustained ones.
///
/// <b>The bout lives in <see cref="StateId.Corroding"/>, whose stacks are the ticks remaining.</b>
/// A bout needs a countdown, and a countdown is not a stack count — but <see cref="EnemyState"/>
/// carries no per-op scratch space, on purpose, or every op would reach for it first. Spending the
/// bout one tick at a time through the ordinary stack machinery costs nothing extra: it ends
/// itself at zero, its clock is dropped with it, and it prints in the debug readout unasked.
/// </summary>
public sealed class Corrode : IOp, ITickingState
{
    private readonly CorrodeTuning _tuning;

    public Corrode(CorrodeTuning tuning = null) => _tuning = tuning ?? new CorrodeTuning();

    public OpId Id => OpId.Corrode;

    /// <summary>The <b>bout</b> ticks, not the bank. Banked stacks do nothing but wait.</summary>
    public StateId State => StateId.Corroding;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        target.AddStacks(StateId.Corrode, (int)Math.Ceiling(quantity * _tuning.StacksPerEnergy));
        TryStartBout(target);
    }

    public double Interval => _tuning.TickInterval;

    /// <summary>
    /// One tick of acid, then one tick off the counter. When that empties the bout, the bank is
    /// checked again immediately — a bout ending with enough stacks left opens the next without
    /// waiting for another shot to land.
    /// </summary>
    public void Tick(EnemyState enemy)
    {
        enemy.DealHpDamage(enemy.Vitals.MaxHp * _tuning.DamageFraction);
        enemy.TakeStacks(StateId.Corroding, 1);

        if (!enemy.IsActive(StateId.Corroding)) TryStartBout(enemy);
    }

    /// <summary>
    /// Open a bout if one is not already running and the bank covers it. Called from both ends —
    /// a shot landing, and a bout finishing — because stacks can cross the threshold either way.
    /// Spends exactly the threshold, so the remainder is credit toward the next one.
    /// </summary>
    private void TryStartBout(EnemyState enemy)
    {
        if (enemy.IsActive(StateId.Corroding)) return;
        if (enemy.Stacks(StateId.Corrode) < _tuning.BoutThreshold) return;

        enemy.TakeStacks(StateId.Corrode, _tuning.BoutThreshold);
        enemy.AddStacks(StateId.Corroding, _tuning.BoutTicks);
    }
}
