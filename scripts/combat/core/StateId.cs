namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// Every state an op can write on an enemy — the rows of
/// <c>docs/tower-design/effect-vocab/vocab-overview/states.md</c>.
///
/// Naming only. A state's <b>shape</b> (ladder vs. flat) is not encoded here: it is decided by
/// which <see cref="EnemyState"/> method writes it, and how it merges follows from that
/// (<c>merge.md</c> — ladders sum with a cap, timed flats take the longer timer).
///
/// <b>R is not here.</b> It is an innate meter, not a written-and-consumed state, so it lives as
/// a field on <see cref="EnemyState"/> (<c>illusion.md</c>).
/// </summary>
public enum StateId
{
    None = 0,

    // ladders — stacks that tick, and that a spend verb can eat
    Burn,
    Chill,
    Corrode,

    // flats — on/off gates, some with a timer, some spent by one consumer
    Freeze,
    Brittle,
    Mark,
    ShieldDown,
    Hexed,
}
