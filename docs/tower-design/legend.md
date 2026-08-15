# Crystal Tower — Symbol & Term Legend

A running glossary for the crystal-dataflow tower system. Symbols are grouped by the
layer they belong to: the **lattice** (shape), the **flow** (routing), the **energy**
(numbers), the **operators** (crystals), and **combos** (the interaction matrix).

---

## Lattice & geometry

| Symbol | Name | Meaning |
|---|---|---|
| ▲ | up-triangle / **split** | A cell that points up. Routes **1 in → 2 out** (divides the post-toll energy among its outputs). |
| ▽ | down-triangle / **merge** | A cell that points down. Routes **2 in → 1 out** (sums its inputs). |
| — | cell | One triangle on the lattice; holds at most one crystal. |
| — | edge | One side of a triangle. Each cell has 3. |
| — | internal edge | An edge shared by two placed crystals. Flow can travel across it. |
| — | open edge | An edge with no placed neighbor — a candidate for a site. |
| — | adjacency | Two crystals **sharing an edge**. The lattice is **bipartite**: ▲ only ever touches ▽, so no two same-orientation cells are adjacent. |
| `r` | same-type count | How many same-type crystals a cell touches across its edges (0–3). |

---

## Flow & routing

| Symbol | Name | Meaning |
|---|---|---|
| — | terminal | A **crystal** (not an edge) that is a boundary: a source or a sink. **Automatic and always on** — derived from geometry, never user-set. Interior crystals are pure crystal↔crystal. |
| **S#** | source | A **source crystal** (green): a **leaf on its input side**, seeded core energy directly (input arity ignored). The core is split **equally** among sources — **no weights**. |
| **T#** | sink | A **sink crystal** (orange): a **leaf on its output side**, drains its post-toll energy to the weapon. Weapon energy = **sum of all sinks**. |
| — | **leaf-input / leaf-output** | Because terminals are auto-derived at leaf sides, this rule holds **by construction**: a crystal's inputs are all-crystal **or** one source; its outputs are all-crystal **or** one sink. A lone crystal is both. |
| **C#** | node label | A crystal's tag in the breakdown/trace panel (bottom→top order). |
| — | **productive** | An edge/node that can still **reach a sink** (downward check). Always true under auto-terminals — not modelled in the compiler. |
| — | **fed** | A node **reachable from a source** (upward check). Always true under auto-terminals — not modelled in the compiler. |
| — | **active edge** | An internal edge that is **productive ∧ fed**, i.e. actually carries routed flow. *Combos fire only on active edges* — but today **every** internal edge is active. The distinction returns when an edge can be switched off (conditional branching). |
| — | conservation routing | The core law: energy is conserved by **local tolls**. ▲ divides the post-toll energy among productive outputs; ▽ sums inputs (`energy-conservation.md`). |

---

## Energy & flow

Energy is the single scalar that flows. There are no per-stat "facets" — see
`energy-conservation.md` for the routing rule.

| Symbol | Name | Meaning |
|---|---|---|
| `E_core` / `ECORE` | core energy | The tower's total energy pool, seeded at the sources. Raised by the **A** investment. |
| `cost` / **draw** | crystal cost | Energy a crystal draws from the stream as it passes (local toll). Separate from its scarce **resource** cost to craft. |
| `eFlow` | net energy | `E_core − Σcost`: energy left after every crystal's toll. Equals **weapon energy** — cost is the only thing that removes energy, and auto-terminals make dead-end branches unbuildable. |
| **op energy** | combo multiplier | Energy arriving at the downstream crystal of an adjacent pair, floored at 0 — the multiplier for that combo-op. |
| **debt** | energy debt | Negative energy in transit. The op multiplies by 0 until a ▽ merge sums it back positive. |
| **weapon energy** | weapon | Sum of all sink energies (floored) — the compiled tower's headline output. |

---

## Operators (crystals)

A crystal has no stat "facet". Orientation on the lattice only sets its **routing arity**
(▲ split / ▽ merge); its effect comes from the **combo** it forms with an adjacent crystal.

| Crystal | Element (flavor) | Draw | Native op |
|---|---|---|---|
| **Ruby** | Fire | 28 | Burn |
| **Sapphire** | Ice | 16 | Chill → Freeze |
| **Emerald** | Nature / Acid | 22 | Corrode |
| **Citrine** | Lightning | 12 | Scramble |
| **Amethyst** | Arcane / Mind | 20 | Mind-damage |
| **Quartz** | Pure | 6 | Purify |

- **Native op** = the crystal's diagonal cell (produced when two of that crystal sit adjacent).
- **Quartz** — pure routing wire; its combos spend a neighbor's ladder (catalyst).
- Roster + combo lookups live in `playground/archive/crystal-core.js`; pairs → ops in
  `effect-vocab/vocab-overview/combo-matrix.md`.

---

## Combos (crystal interaction)

| Symbol | Name | Meaning |
|---|---|---|
| — | combo | The op produced by two crystals **adjacent in the flow**. Looked up in the combo matrix. |
| `COMBO[A][B]` | combo matrix | Symmetric table over the 6 crystals; `COMBO[A][B]` names the op that pair produces. Source of truth: `effect-vocab/vocab-overview/combo-matrix.md`. |
| — | native op | The diagonal `COMBO[A][A]` — a crystal's single-crystal op, produced by two of that crystal adjacent. |
| **op energy** | combo multiplier | The energy arriving at the downstream crystal scales the combo-op (floored at 0). |
| — | **ordered op list** / shot | The compiled shot: an **ordered** `(op, qty)` list, one entry per active combo. Not a bag; **not consumed at compile** — the enemy spends it at hit time (`impl-planning/upgrades/op-flow.md`). |
| — | **op order** | Eval order of the shot: **vertical first** (lowest gem first ⇒ a higher gem is always last), **horizontal second** (leftmost first), anchored to each op's producing (downstream) gem. |

**Key rule:** a combo only fires on an **active** internal edge — flow must actually cross
the junction. Merely touching does nothing. Op *behavior* is authored per-op under
`effect-vocab/ops/`; the matrix here only names them.

---

## Investment axes

| Axis | What it raises | How |
|---|---|---|
| **A — energy ceiling** | `E_core` | Upgrade the core for a bigger energy pool. |
| **Space economy** | lattice cells | Buy room to fit more crystals / complete patterns. |
| **B — generators** | net energy | (Planned) crystals that *add* to the pool instead of taxing it. Balanced by finite cells + resource cost. |

---

*Conventions:* `▲ = split = 1→2 = divide`, `▽ = merge = 2→1 = sum`. Flow runs strictly
upward (a DAG). Effects only occur on **active** routes.