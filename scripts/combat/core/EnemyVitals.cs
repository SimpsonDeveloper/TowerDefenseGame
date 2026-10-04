namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// The enemy stats an op is allowed to read — innate numbers, not live ones.
///
/// This is the whole of what crosses into the combat core from the enemy's side. **Current HP is
/// deliberately absent**: damage goes one way, queued out of <see cref="EnemyState"/> to a
/// <c>HealthComponent</c> the core cannot see, and letting an op read the bar back would quietly
/// make effects depend on the order damage happened to land in a frame. Max HP is a stat, so it
/// is here; current HP is a consequence, so it is not.
///
/// Ops need this because their curves are relative to the enemy, not absolute: Chill's freeze
/// threshold scales off max HP so a big enemy is harder to freeze, and Corrode ticks a percentage
/// of it so a big health pool is not immune to acid
/// (<c>effect-vocab/ops/primitives/chill-freeze.md</c>, <c>corrode.md</c>).
///
/// Grow it by adding a parameter — armor when Corrode eats it. Nothing is added before an op
/// actually reads it: a stat wired to a placeholder is worse than one that is missing.
/// </summary>
/// <param name="MaxHp">The enemy's full health pool.</param>
/// <param name="MaxMind">Illusion resistance at full (<c>effect-vocab/vocab-overview/illusion.md</c>).</param>
/// <param name="MoveSpeed">Unmodified speed in px/s — what a slow scales <b>from</b>. Captured
///   once, so writing a slowed value back onto the enemy cannot compound frame over frame.</param>
public sealed record EnemyVitals(double MaxHp = 100, double MaxMind = 100, double MoveSpeed = 0);
