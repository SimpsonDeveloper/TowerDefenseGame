using System.Collections.Generic;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// Spends a compiled shot on one enemy — the runtime half of the triad, and the far end of the
/// seam the compiler has been feeding since roadmap item 2.
///
/// The list arrives already ordered (lower gem first, leftmost first); this walks it and applies
/// each op <b>one at a time</b>, so every op reads the enemy as the ops below it left it. That
/// sequencing is the whole mechanism behind interactives — Frostburn finds the Burn that ran a
/// step earlier and converts it — and it is why nothing is re-sorted here
/// (<c>vocab-overview/states.md</c> → *Shot resolution*).
/// </summary>
public static class ShotResolver
{
    /// <summary>
    /// Apply every op in the shot, in order. The whole list resolves in this one call — the
    /// player never sees the states between two ops, only the net result.
    /// </summary>
    public static void Resolve(IReadOnlyList<ShotOp> shot, EnemyState target, CombatRules rules = null)
    {
        if (shot == null || target == null) return;

        rules ??= CombatRules.Default;

        for (int i = 0; i < shot.Count; i++)
        {
            ShotOp entry = shot[i];
            IOp op = rules.Op(entry.Op);
            op?.Apply(new ShotContext(shot, i), entry.Quantity, target);
        }
    }
}
