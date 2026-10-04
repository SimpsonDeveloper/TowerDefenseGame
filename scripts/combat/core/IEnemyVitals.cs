namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// What an op may read about the enemy — the <b>port</b> the engine-free core sees the engine
/// through. Implemented on the Godot side by whatever actually owns each number, so the values are
/// live: no snapshot, no mirror, nothing to go stale.
///
/// <b>Read-only on purpose, and writes stay queued.</b> Not for purity — for batching. A burn
/// ticking six times a second through a write-through port would fire six <c>Damaged</c> signals
/// and six damage flashes; queueing collapses that into one <c>TakeDamage</c> a frame
/// (<see cref="EnemyState.TakeHpDamage"/>).
///
/// Everything here is readable because there is no principled line that keeps an op away from a
/// number: <c>short-circuit.md</c> already describes an "execute burst", and an execute reads
/// current HP by definition. Ops that have no business reading something simply do not.
/// </summary>
public interface IEnemyVitals
{
    /// <summary>Current health. Live — it moves as the enemy is shot.</summary>
    double Hp { get; }

    /// <summary>The full health pool. What Chill's freeze threshold and Corrode's tick scale off.</summary>
    double MaxHp { get; }

    /// <summary>
    /// Current illusion resistance (<c>effect-vocab/vocab-overview/illusion.md</c>). Live, and read
    /// by ops: Numb scales the freeze threshold off it, so a mind-damage build silently makes a
    /// freeze build cheaper.
    /// </summary>
    double Mind { get; }

    /// <summary>Illusion resistance at full.</summary>
    double MaxMind { get; }

    /// <summary>Unmodified speed in px/s — what a slow scales <b>from</b>.</summary>
    double MoveSpeed { get; }
}
