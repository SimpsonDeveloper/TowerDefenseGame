# Tower Upgrade + Combat — Implementation Roadmap

Master build sequence for the crystal-lattice tower in Godot 4.5 / C#. Two tracks:

- **upgrades/** — building & compiling a tower's lattice (the player's "upgrade" surface).
  Includes **op-flow** (`upgrades/op-flow.md`): op payloads are part of what the compiler
  *emits*, so they live with the compiler, not combat.
- **combat/** — making the compiled ops actually affect enemies.

Design source of truth is one level up (`../compilation-system.md`,
`../energy-conservation.md`, `../effect-vocab/vocab-overview/combo-matrix.md`). This tree
is *how we build it*, not new mechanics — except two rules first stated here and flagged
for back-porting: **op-metadata flow** and **leaf-node outputs** (`upgrades/op-flow.md`).

Status: planning. Nothing built.

---

## Ground facts

- **Each tower owns its own crystal lattice.** The lattice compiles to a cached result;
  that result computes what **each shot does when it hits an enemy**.
- The compiler is **engine-free C#** (unit-testable); Godot is a thin shell over it.
- **Sources and sinks are crystal-level terminals** at leaf sides only (leaf-input /
  leaf-output) — see `upgrades/op-flow.md` §3.

---

## Sequence

| # | Track | Item | Doc | Depends | Size |
|---|---|---|---|---|---|
| 1 | upgrades | Compiler core + **op-flow** (ops + payloads at leaf outs) | `upgrades/op-flow.md` · `upgrades/compiler-core.md` | — | M |
| 2 | upgrades | Lattice UI + template editor | `upgrades/lattice-ui.md` | 1 | L |
| 3 | combat | First primitive op behaviors | `combat/primitives.md` | 1 | M |
| 4 | combat | Enemy R + paths / roads / deviation | `combat/enemy-r.md` | 3 | XL |
| 5 | — | Delivery shapes · impact cap · investment axes | *(later)* | 3 | — |

Size: S/M/L/XL rough effort. **Op-flow leads item 1** — the first deliverable is a
compiler that emits the correct ops with correct energy multipliers at each leaf output
(names + producer/consumer wiring only, 1-to-1 conversion; no op *behavior* yet).

---

## Ordering rationale

- **1 first (op-flow + compiler).** Op-flow is deliberately thin — names, producer/consumer
  wiring, 1-to-1 conversion — but it can't produce multipliers without the energy engine, so
  it and the compiler core are **one milestone**. This is the foundation everything downstream
  reads: it fixes what each shot *carries*.
- **1 before 2.** The UI's live preview *is* the core compiler — build and test the engine
  headless first, then render it. UI without a trustworthy compiler is guesswork.
- **Then combat, 3 → 4.**
  - **3 (primitives)** is where compilation first *does something* visible in-game — it wires
    the op names already flowing out of item 1 to real enemy effects.
  - **4 (enemy R)** is the largest scope: it needs the R meter **and** preset enemy paths
    **and** player-placed roads **and** deviation, integrated with tower placement. Last.

---

## Back-port TODO (design docs to reconcile once these land)

- ✅ **Terminal rules** (leaf-input / leaf-output, crystal-level sources/sinks) → back-ported
  to `../compilation-system.md` §2, `../legend.md`, `upgrades/op-flow.md` §3,
  `upgrades/compiler-core.md`; enforced structurally in the playground.
- ✅ **Op-metadata flow** → `../compilation-system.md` §3 now carries the payload channel
  (energy is not the only thing that flows; op payloads flow and get consumed).