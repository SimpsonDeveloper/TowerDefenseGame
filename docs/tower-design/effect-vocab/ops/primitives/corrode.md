# Op: Corrode

**Kind:** primitive — applies a state (threshold DoT)
**Applies:** Corrode · **State merge:** sum stacks, **no cap**

---

## Definition

Emerald's rate applies **Corrode stacks** — acid that has to pool before it eats.

- **Threshold, then spend.** Stacks bank harmlessly until they reach `X`. At `X` the op spends
  `X` of them and starts a bout: **`Y`% of the enemy's max HP per tick, for 10 ticks.**
- **Leftovers stay banked.** Spending `X` off a pile of `X + n` leaves `n` toward the next bout,
  so sustained fire chains bouts instead of wasting the overflow.
- **State merge:** two Corrode sources sum stacks. **No cap.**

This is a different shape from Burn on purpose. Burn is immediate and fades; Corrode is delayed
and flat. Burn rewards one big hit, Corrode rewards sustained ones — and because its damage is a
percentage of max HP, it is the answer to a health pool Burn's flat numbers cannot dent.

## Numbers

**Not authored.** Open knobs: `X` (stacks per bout) · `Y` (% max HP per tick) · tick interval ·
whether banked stacks decay while waiting. The 10-tick bout length is itself tunable.

## Status

**Not built.** `OpId.Corrode` exists and rides shots today; nothing is registered for it, so it
resolves as a no-op (`../../../impl-planning/combat/primitives.md`).

Max HP is readable: `EnemyState.Vitals.MaxHp` (`../../../impl-planning/combat/primitives.md` §3).

One thing is still open. **A bout has a countdown of its own**, separate from the stacks, and
`EnemyState` carries no per-op scratch space — its clock ticks an op, it does not hold an op's
private bookkeeping. Planned answer: give the bout **its own `StateId` whose stack count is the
ticks remaining**. No new machinery, and "9 ticks of acid left" is honestly a state.
