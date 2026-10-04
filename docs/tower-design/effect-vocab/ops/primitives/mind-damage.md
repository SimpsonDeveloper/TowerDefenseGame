# Op: Mind-damage (Illusion)

**Kind:** primitive — routes damage to a bar
**Affects:** R (illusion resistance) · not a stacking state

---

## Definition

An **Am–Am combo** (two amethyst crystals in sequence — this native op) routes damage to the
enemy's **R bar** instead of HP. A single amethyst does not: reaching the mind takes two in
sequence. It drains **R** — the "mind shield" — permanently; at R = 0 the enemy is railroaded and
can never deviate.

- Not a stacking status — damage to a meter, like HP damage but on the R bar. No merge rule (R is
  an innate per-enemy meter, only drained, never spread).
- **Magnitude is the op's quantity**, the energy that crossed the producing edge — the same rule
  every other op follows.
- To hit both HP and R, split the stream and route one branch through an Am–Am combo. The energy
  divides, so reach into one bar is paid for out of the other.

## Numbers

Ported to `scripts/combat/core/ops/MindDamage.cs` as `MindDamageTuning`.

| Knob | Default | What it does |
|---|---|---|
| `RPerEnergy` | `1/20` | R drained per unit of edge energy. |

The whole tuning surface: no state, no curve, no duration. Against the shipped starter turret's
150 core, an Amethyst·Amethyst edge carries ~130 — **6.5 R a shot**, so about 16 shots to break a
100-R enemy and 60 to break a 400-R one.

Unlike Corrode's percentage, the magnitude is **absolute**: the same shot is worth proportionally
less against a bigger meter, which is what makes a tough mind a real investment to break.

**Placeholder, not balance.** Real balance lives in `Rmax` / `Pmax` / roll interval
(`../../vocab-overview/illusion.md`). The tests pin their own value
(`tests/CrystalCore.Tests/MindDamageTests.cs`).

## Magnitude: a model correction

This file used to say the op "tags a stream's `type → mind`" and took its magnitude from the
stream's **power** stat. **The compiler never grew stream stats.** It routes energy and names ops,
and what it emits per edge is `(OpId, quantity)` — so quantity is the magnitude here exactly as it
is for Burn, Corrode and Chill. The typed-stream phrasing is gone; the behaviour it described is
unchanged, since the energy on an Am–Am edge is the same number the old wording meant.

## Status

**Built** — `scripts/combat/core/ops/MindDamage.cs`. The simplest op in the vocabulary: one
`IOp` face, one line of arithmetic, no state and no tick.

**Its payoff does not exist yet.** R at 0 means the enemy can never deviate, and deviation is
roadmap item 5 (`../../../impl-planning/combat/enemy-r.md`). Until then draining R changes nothing
an enemy does. `EnemyStateDebug` prints `R 93.5/100` so the drain is at least visible.
