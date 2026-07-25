# Crystal Tower — Effect Vocabulary — Overview (v2)

Working version and **index**. This file holds the core principle and the triad
definition; every other topic is split into its own file in this directory. Op
definitions (prose) live one-per-file in `../ops/`; archived versions in
`../vocab-archive/`.

## Contents

| File | Covers |
|---|---|
| `states.md` | the states table · reusable shapes · many-to-many · state coupling |
| `merge.md` | the two merges — lattice ▽ (compile-time) vs state merge (runtime) |
| `illusion.md` | illusion resistance (R) mechanic · deviation math `f(R)` |
| `damage.md` | damage types & bars · hitting both bars |
| `shield.md` | Shield defensive layer · Shield-down |
| `combo-matrix.md` | crystal roster + the N×N combo → op matrix (source of truth) |
| `delivery.md` | delivery is owned by the tower type (separate axis) |
| `open-questions.md` | unresolved rulings & tuning knobs |
| `../ops/` | per-op prose definitions (`../ops/README.md` indexes them) |
| `../vocab-archive/` | frozen prior versions |

**Scope of this version:** states, producers, consumers. **Delivery is not a crystal
effect** — it belongs to the **tower type** (`delivery.md`). Crystals supply *what the
hit does* (state writes + consumers); the tower type supplies *how the hit is
delivered*. The two axes are orthogonal.

---

## Core principle

You author a **finite vocabulary** of triads + typed interaction rules. The
player's **reachable build-space** is the emergence. Emergence lives in the
*sentences* (builds), never in new *words* (statuses). No producing undefined
statuses — every status is authored, detectable, and consumed.

## The triad

The authored unit is the **triad**, not the lone status:

`producer → detectable state → consumer`

- **Producer** — a crystal or crystal combo that writes a detectable state on an enemy.
- **State** — a flag/meter the enemy carries. May be a small FSM (flag + counter +
  timer). The only "derivation" is a counter crossing a threshold. No physics sim.
- **Consumer** — a crystal or combo that reads the state and does something
  **interactive** (non-additive).

### Two op classes

- **Primitive ops** apply a state or a base effect and **stand alone** — they need no
  pre-existing state on the target (Burn, Chill, Corrode, Mark, Scramble, mind-damage).
  They are **exempt** from the interactive test: nothing to "remove and compare," they
  are atomic.
- **Interactive ops** are **reactive** — they read/consume a state and do something
  conditional (Shatter, Flareup, Dissolve, Frostburn, Detonate, Focus, Hex). These are
  the triads, and **every one must pass the interactive test** below.

The line is **reactivity, not crystal count**: Burn is a two-crystal combo but still a
primitive (it consumes no state). Per-class doc rules live in `../ops/README.md`.

### The interactive test

The gate every **interactive** op must pass. Remove one part of the triad — does the
other's behavior change?

- **No → additive.** Reject as a fake combo (just two stats summed, e.g. "piercing burn").
- **Yes → interactive.** Keep (e.g. "Shatter a frozen enemy for burst scaling with
  chill stacks" — the freeze changes what shatter *does*).

Hard authoring rule **for interactive ops**: none enters the vocabulary without
passing. Each interactive op-file (`../ops/*.md`) restates its own proof in a required
**Interactive** section (the per-op "remove one part" argument), the same way every op
carries **Open knobs**. **Primitive op-files skip it.** If an interactive op can't
state the proof, it isn't an op yet.

**Conditional ops.** Many consumers are **inert without their prerequisite state** —
they do nothing on an enemy that lacks it. Detonate needs Mark; Shatter needs Freeze
or Brittle; Flareup needs Burn; Frostburn needs both Burn and Chill. This is normal:
the consumer half of a triad only fires when its state is present. Building the
producer and consumer without ever landing the state on the same enemy = a dud build.

The concrete vocabulary — states, crystals, combos — lives in the topic files listed
above. Start with `states.md`.
