# Tower Upgrade + Combat — Implementation Roadmap

Master build sequence for the crystal-lattice tower in Godot 4.5 / C#. Two tracks:

- **upgrades/** — building & compiling a tower's lattice (the player's "upgrade" surface).
- **combat/** — making the compiled ops actually affect enemies.

Design source of truth is one level up (`../compilation-system.md`,
`../energy-conservation.md`, `../effect-vocab/vocab-overview/combo-matrix.md`). This tree
is *how we build it*, not new mechanics — except two rules first stated here and flagged
for back-porting: **op-metadata flow** and **leaf-node outputs** (`combat/op-flow.md`).

Status: planning. Nothing built.

---

## Ground facts

- **Each tower owns its own crystal lattice.** The lattice compiles to a cached result;
  that result computes what **each shot does when it hits an enemy**.
- The compiler is **engine-free C#** (unit-testable); Godot is a thin shell over it.
- **Outputs (sinks) live at leaf nodes only** — new constraint, see `combat/op-flow.md`.

---

## Sequence

| # | Track | Item | Doc | Depends | Size |
|---|---|---|---|---|---|
| 1 | upgrades | Compiler core + tests | `upgrades/compiler-core.md` | — | M |
| 2 | upgrades | Lattice UI + template editor | `upgrades/lattice-ui.md` | 1 | L |
| 3 | combat | Op-metadata flow (payloads) | `combat/op-flow.md` | 1 | S–M |
| 4 | combat | First primitive op behaviors | `combat/primitives.md` | 3 | M |
| 5 | combat | Enemy R + paths / roads / deviation | `combat/enemy-r.md` | 4 | XL |
| 6 | — | Delivery shapes · impact cap · investment axes | *(later)* | 4 | — |

Size: S/M/L/XL rough effort.

---

## Ordering rationale

- **1 before 2.** The UI's live preview *is* the core compiler — build and test the engine
  headless first, then render it. UI without a trustworthy compiler is guesswork.
- **After 1 + 2, do the combat track in order 3 → 4 → 5.** Of the three candidate
  starting points:
  - **3 (op-flow)** is foundational and deliberately simple — until op payloads propagate
    through the lattice, primitives have nothing to carry. Do it first.
  - **4 (primitives)** is where compilation first *does something* visible in-game.
  - **5 (enemy R)** is the largest scope: it needs the R meter **and** preset enemy paths
    **and** player-placed roads **and** deviation, integrated with tower placement. Last.

---

## Back-port TODO (design docs to reconcile once these land)

- **Leaf-node output rule** → `../compilation-system.md` §2/§5, `../legend.md`, and the
  playground (which currently allows a sink on any open edge).
- **Op-metadata flow** → `../compilation-system.md` §3/§7 (energy is not the only thing
  that flows; op payloads flow and get consumed).