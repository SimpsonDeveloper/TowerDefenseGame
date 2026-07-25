# Ops — definitions

One file per authored op. **Definition only** — mechanics, conditions, formula, and
(for interactive ops) the interactive proof + open knobs. The framework, tables, and
shared mechanics live in `../vocab-overview/overview.md`; each op here is linked from
the overview's tables.

Pattern: filename = op slug (lowercase). An op earns a file once it needs prose beyond
its combo-table row in the overview.

## Folder = class

Op **class is encoded by folder**, not a field:

- **`interactives/`** — **reactive** ops: read/consume a state, do something conditional.
  Each is a triad and **must pass the interactive test**
  (`../vocab-overview/overview.md` → *The interactive test*).
- **`primitives/`** — ops that **apply a state or base effect and stand alone** (no
  pre-existing state required). Atomic. **Exempt** from the interactive test.

The split is **reactivity, not crystal count** — Burn is a two-crystal combo but still
a primitive (it consumes no state).

## Required op-doc structure

Every op file, in order:

1. **Header block** — kind · prerequisites / states touched. (Class is the folder;
   the crystal combo lives in `../vocab-overview/combo-matrix.md`, not here.)
2. **Definition** — what it does (prose).
3. **`## Interactive`** — the per-op "remove one part" proof. **`interactives/` only;
   primitives omit it.** If an interactive op can't state it, it isn't an op yet.
4. **`## Open knobs`** — unresolved tuning values (self-explanatory, like this list).

Optional extra sections (e.g. Hex's *Why merge matters*) are fine after these.

Combos (which crystals make each op) live in `../vocab-overview/combo-matrix.md` — the
source of truth. These tables index files only.

## Index — `interactives/`

| Op | File | Reacts to |
|---|---|---|
| Frostburn | `interactives/frostburn.md` | Burn (→ chill stacks) |
| Shatter | `interactives/shatter.md` | Freeze / Brittle (→ burst) |
| Flareup | `interactives/flareup.md` | Burn (→ burst) |
| Dissolve | `interactives/dissolve.md` | Corrode (→ execute + Brittle) |
| Detonate | `interactives/detonate.md` | Mark (→ burst) |
| Focus | `interactives/focus.md` | Mark (→ retarget gate) |
| Hex | `interactives/hex.md` | on-death → spreads carried states |
| Fire Arc | `interactives/fire-arc.md` | Burn (→ chains between burning enemies) |
| Frost Arc | `interactives/frost-arc.md` | Chill (→ chains between chilled enemies) |
| Acid Arc | `interactives/acid-arc.md` | Corrode (→ chains between corroded enemies) |
| Numb | `interactives/numb.md` | R (→ lowers freeze threshold) |
| Accelerant | `interactives/accelerant.md` | any DoT (→ speeds its tick rate) |
| Weather | `interactives/weather.md` | Freeze removal (→ escalating damage) |
| Short-circuit | `interactives/short-circuit.md` | Shield-down (→ execute burst) |

## Index — `primitives/`

| Op | File | Applies |
|---|---|---|
| Burn | `primitives/burn.md` | Burn (ignition DoT) |
| Chill → Freeze | `primitives/chill-freeze.md` | Chill stacks → Freeze |
| Corrode | `primitives/corrode.md` | Corrode |
| Mark / Sigil | `primitives/mark.md` | Mark |
| Scramble | `primitives/scramble.md` | Shield-down |
| Mind-damage (Illusion) | `primitives/mind-damage.md` | drains R |
| Purify | `primitives/purify.md` | strips enemy buffs *(pending buff system)* |
