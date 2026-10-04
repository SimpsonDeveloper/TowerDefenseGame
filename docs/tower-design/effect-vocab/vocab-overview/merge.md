# Merge

Part of the Effect Vocabulary overview — see `overview.md` for the index.

Two separate operations share the word "merge." Keep them apart.

## Lattice merge (▽ — compile-time, on the tower)

Merges **energy**. A ▽ sums what its two inputs hand up; **always sum** (conservation
routing). This is weapon-building, before anything touches an enemy.

There are no other stream stats. An earlier model had power, slow, rate and a mind
magnitude riding the stream and merging alongside energy; the compiler never grew them
(`../../impl-planning/upgrades/compiler-core.md`). Every op's magnitude is instead the
**energy on the edge that produced it** — Burn's stacks, Corrode's stacks, Chill's
stacks and Mind-damage's Mind drain all read the same number.

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
- **Meter** (Mind) → **n/a**: innate, per-enemy, only drained — never spread.

## Don't conflate them

They can coincide numerically (Chill state-merge sums; the lattice also sums) but they
are **different operations in different spaces**. **Mind makes the split visible:** the
energy feeding an Am–Am edge *sums in the lattice* like any other, yet Mind itself *does not
spread between enemies* (state-merge n/a). One is about building the weapon; the other is
about what two weapons do to one enemy.
