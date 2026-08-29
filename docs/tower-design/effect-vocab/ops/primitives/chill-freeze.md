# Op: Chill → Freeze

**Kind:** primitive — applies a state (ladder slow → stop)
**Applies:** Chill stacks → Freeze · **State merge:** sum stacks, **no cap**

---

## Definition

Sapphire applies **Chill stacks**, each adding slow. Standalone.

- **Ladder** (slow → stop): stacks accumulate slow; past the freeze threshold `Tfreeze` the enemy
  **Freezes** — stops moving and skips deviation rolls.
- **The threshold scales with the enemy's max HP.** A bigger enemy is harder to freeze, so the
  same stack pile that locks a small one merely slows a large one. This is Chill's defining
  asymmetry and the reason its curve is not Burn's.
- **State merge:** two Chill sources sum stacks. **No cap** — stacks keep accruing past `Tfreeze`
  and the overstack is still tracked, which is what a second freeze has to chew through.

## Numbers

**Not authored.** Open knobs: slow per stack · how `Tfreeze` scales off max HP · freeze duration ·
whether stacks decay on their own and how fast.

## Status

**Not built.** `OpId.ChillFreeze` exists and rides shots today; nothing is registered for it, so
it resolves as a no-op (`../../../impl-planning/combat/primitives.md`).

Max HP is readable: `EnemyState.Vitals.MaxHp` (`../../../impl-planning/combat/primitives.md` §3).
Current HP is not, and the threshold scales off **max** — so nothing structural is in the way.

Still missing for the slow itself: an enemy's base move speed is not in `EnemyVitals` yet, and
nothing gives a state a way to modify movement. Both land when Chill is built.
