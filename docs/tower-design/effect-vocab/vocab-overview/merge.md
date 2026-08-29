# Merge

Part of the Effect Vocabulary overview — see `overview.md` for the index.

Two separate operations share the word "merge." Keep them apart.

## Lattice merge (▽ — compile-time, on the tower)

Merges **stream stats** — power, slow, rate, and the mind magnitude bound for R.
**Always sum** (conservation routing). This is weapon-building, before anything touches
an enemy.

## State merge (runtime, on the enemy)

Combines two *applied* instances of a **carried state** — two towers stacking Chill, or
Hex spreading states onto a neighbor that already has them (`../ops/interactives/hex.md`).
The rule **varies by state shape**:

- **Stack / ladder** (Chill, Burn, Corrode) → **sum** stacks, **uncapped**. What stops a pile
  being dangerous forever is the op's own decay or spend curve, authored per op under
  `../ops/primitives/`, not a ceiling here. Burn eats its stacks as it ticks; Corrode spends
  them at a threshold.
- **Timed flat** (Hexed) → **max** timer (refresh).
- **Flat on/off** (Brittle, Mark, Shield-down) → **OR** (present wins).
- **Meter** (R) → **n/a**: innate, per-enemy, only drained — never spread.

## Don't conflate them

They can coincide numerically (Chill state-merge sums; the lattice also sums) but they
are **different operations in different spaces**. **R makes the split visible:** its
magnitude *sums in the lattice* (it's a stat), yet R itself *does not spread between
enemies* (state-merge n/a).
