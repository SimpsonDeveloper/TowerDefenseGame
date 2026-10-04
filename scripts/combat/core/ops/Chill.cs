using System;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core.ops;

/// <summary>
/// Chill's numbers, gathered so tuning is a data edit. Authored alongside the behaviour in
/// <c>effect-vocab/ops/primitives/chill-freeze.md</c> — that file is the source, this record is
/// the port.
/// </summary>
/// <param name="StacksPerEnergy">Stacks per unit of edge energy, rounded up. Sapphire·Sapphire off
///   the 150-core starter turret delivers ~134, which at 1/20 is ~7 stacks a shot.</param>
/// <param name="SlowPerStack">Speed lost per stack held.</param>
/// <param name="MinSpeedScale">Floor on the slow, so chill alone never fully stops an enemy —
///   stopping is Freeze's job, and it has to be earned.</param>
/// <param name="FreezeThreshold">Stacks to freeze an enemy of <see cref="ReferenceMaxHp"/> health.</param>
/// <param name="ReferenceMaxHp">The health pool <see cref="FreezeThreshold"/> is quoted against.
///   The real threshold scales from here, which is what makes a bigger enemy harder to freeze.</param>
/// <param name="FreezeDuration">Seconds an enemy stays frozen.</param>
/// <param name="DecayPerTick">Stacks lost per tick while nothing is refreshing them.</param>
/// <param name="TickInterval">Seconds between decay ticks.</param>
public sealed record ChillTuning(
    double StacksPerEnergy = 1.0 / 20.0,
    double SlowPerStack = 0.03,
    double MinSpeedScale = 0.25,
    int FreezeThreshold = 20,
    double ReferenceMaxHp = 200,
    double FreezeDuration = 2.0,
    int DecayPerTick = 1,
    double TickInterval = 1.0);

/// <summary>
/// <b>Chill → Freeze</b> (Sapphire · Sapphire) — a ladder that slows, then stops, per
/// <c>effect-vocab/ops/primitives/chill-freeze.md</c>.
///
/// Stacks slow the enemy while they are held, down to a floor. Past a threshold the enemy
/// <b>Freezes</b> outright, and the freeze <b>spends</b> that many stacks — the overstack survives
/// as credit toward the next one, the same bank-and-spend shape Corrode uses for its bouts. If
/// freezing left the stacks in place the enemy would re-freeze forever.
///
/// <b>The threshold scales with max HP.</b> A bigger enemy is harder to freeze, so the same pile
/// that locks down a small one merely slows a large one. This is Chill's defining asymmetry and
/// the reason its curve is not Burn's: Burn's damage is flat and Corrode's is proportional, while
/// Chill's *cost* is proportional.
///
/// Three roles in one class: the shot applies stacks, the tick decays them, and the movement
/// modifier turns what is left into a speed multiplier. The multiplier is derived every time it is
/// read, so a chill wearing off restores speed on its own.
/// </summary>
public sealed class Chill : IOp, ITickingState, IMovementModifier
{
    private readonly ChillTuning _tuning;

    public Chill(ChillTuning tuning = null) => _tuning = tuning ?? new ChillTuning();

    public OpId Id => OpId.ChillFreeze;

    public StateId State => StateId.Chill;

    public double Interval => _tuning.TickInterval;

    public void Apply(ShotContext context, double quantity, EnemyState target)
    {
        if (quantity <= 0) return;

        target.AddStacks(StateId.Chill, (int)Math.Ceiling(quantity * _tuning.StacksPerEnergy));
        TryFreeze(target);
    }

    /// <summary>
    /// Stacks bleed off when nothing is topping them up, and the threshold is re-checked — a
    /// freeze that expires with enough left over refreezes without waiting for another shot.
    /// </summary>
    public void Tick(EnemyState enemy)
    {
        enemy.TakeStacks(StateId.Chill, _tuning.DecayPerTick);
        TryFreeze(enemy);
    }

    /// <summary>Slow is linear in the stacks held, floored so chill alone cannot stop anything.</summary>
    public double SpeedScale(EnemyState enemy)
    {
        double scale = 1 - enemy.Stacks(StateId.Chill) * _tuning.SlowPerStack;
        return scale < _tuning.MinSpeedScale ? _tuning.MinSpeedScale : scale;
    }

    /// <summary>
    /// Stacks needed to freeze <i>this</i> enemy, scaled off its health pool. Never below 1, or a
    /// trivially weak enemy would freeze on an empty hit.
    /// </summary>
    public int ThresholdFor(EnemyState enemy)
    {
        double scaled = _tuning.FreezeThreshold * (enemy.Vitals.MaxHp / _tuning.ReferenceMaxHp);
        return scaled < 1 ? 1 : (int)Math.Ceiling(scaled);
    }

    private void TryFreeze(EnemyState enemy)
    {
        if (enemy.IsActive(StateId.Freeze)) return;

        int threshold = ThresholdFor(enemy);
        if (enemy.Stacks(StateId.Chill) < threshold) return;

        enemy.TakeStacks(StateId.Chill, threshold);
        enemy.SetFlag(StateId.Freeze, _tuning.FreezeDuration);
    }
}

/// <summary>
/// <b>Freeze</b> — the stop itself, as its own movement modifier.
///
/// Separate from <see cref="Chill"/> because a modifier is keyed by the state it reads, and these
/// are two states: chill slows while it is held, freeze halts while it is up. It is no op of its
/// own — nothing produces Freeze but Chill crossing its threshold — so it carries no
/// <see cref="IOp"/> face, and <see cref="EnemyState"/> expires its timer like any flat state.
///
/// Shatter will consume it (<c>effect-vocab/ops/interactives/shatter.md</c>). Skipping deviation
/// rolls, the other half of what freezing means, waits on roadmap item 5.
/// </summary>
public sealed class Frozen : IMovementModifier
{
    public StateId State => StateId.Freeze;

    public double SpeedScale(EnemyState enemy) => 0;
}
