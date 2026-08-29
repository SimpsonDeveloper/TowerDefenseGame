using System.Collections.Generic;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// What one op does when a shot lands. One implementation per <see cref="OpId"/>; an op with no
/// implementation is a no-op, which is what lets the whole pipeline run with 1 of 21 ops written
/// (<c>impl-planning/combat/primitives.md</c>).
///
/// <b>Consumers resolve here, not in the compiler.</b> An interactive reads the enemy's state as
/// it stands at its own step — after everything lower in the shot has already written — and
/// spends it with <see cref="EnemyState.TakeStacks"/>. That ordering is the compiler's output
/// and this is where it is cashed in (<c>vocab-overview/states.md</c> → *Shot resolution*).
/// </summary>
public interface IOp
{
    OpId Id { get; }

    /// <param name="context">Where in the shot this op sits. Ops that only read enemy state
    ///   ignore it; it exists because order is load-bearing and an op may need to know its own.</param>
    /// <param name="quantity">Energy that arrived at the producing gem — the op's magnitude,
    ///   already floored at 0 by the compiler.</param>
    /// <param name="target">The enemy, and the only thing an op may mutate.</param>
    void Apply(ShotContext context, double quantity, EnemyState target);
}

/// <summary>
/// The shot an op is being applied as part of, and its index in it. Deliberately thin: an op's
/// input is the enemy's <i>current</i> state, not the list — the list only says who ran first.
/// </summary>
public readonly record struct ShotContext(IReadOnlyList<ShotOp> Shot, int Index);

/// <summary>
/// How a carried state behaves <b>between</b> shots — the ticking half. Burn writes stacks
/// through <see cref="IOp"/> and then bleeds HP through this, so one primitive is usually one
/// class implementing both.
///
/// <b>One call, and the op decides everything.</b> <see cref="EnemyState"/> owns only the clock;
/// it does not know what a tick costs, how fast a state decays, or when it should end. Burn
/// spends its own stacks on each tick and dies when they run out; Corrode will bank stacks until
/// a threshold and then spend the lot. Neither shape is visible from the outside.
///
/// Unregistered means it never ticks. The state is still carried and still readable by consumers
/// — the same "missing handler = no-op" rule the op registry follows.
/// </summary>
public interface ITickingState
{
    StateId State { get; }

    /// <summary>
    /// Seconds between ticks. Fixed per op: the clock is armed when the state first lands and
    /// keeps its phase from there, so two ops on the same interval stay offset by when each
    /// arrived rather than firing together. Zero means never.
    /// </summary>
    double Interval { get; }

    /// <summary>
    /// One tick. Read whatever state matters, deal damage, spend stacks — including its own,
    /// which is how a state ends itself.
    /// </summary>
    void Tick(EnemyState enemy);
}
