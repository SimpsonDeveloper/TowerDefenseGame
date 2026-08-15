# Tower Upgrade + Combat — Implementation Roadmap

Master build sequence for the crystal-lattice tower in Godot 4.5 / C#. Two tracks:

- **upgrades/** — building & compiling a tower's lattice (the player's "upgrade" surface).
  Includes **op-flow** (`upgrades/op-flow.md`): op payloads are part of what the compiler
  *emits*, so they live with the compiler, not combat.
- **combat/** — making the compiled ops actually affect enemies.

Design source of truth is one level up (`../compilation-system.md`,
`../energy-conservation.md`, `../effect-vocab/vocab-overview/combo-matrix.md`). This tree
is *how we build it*, not new mechanics — two compiler rules are detailed here and back-ported
up: the **ordered op list** (`upgrades/op-flow.md`) and **auto source/sink terminals + equal
split** (`upgrades/compiler-core.md`).

Status: items **1–2 built** (`scripts/towers/crystal/core/`, tests in
`tests/CrystalCore.Tests/`); items 3–6 planning.

---

## Ground facts

- **Each tower owns its own crystal lattice.** The lattice compiles to a cached result;
  that result computes what **each shot does when it hits an enemy**.
- The compiler is **engine-free C#** (unit-testable); Godot is a thin shell over it.
- **Sources and sinks are automatic crystal-level terminals** at leaf sides only (leaf-input /
  leaf-output), always on, no weights — see `upgrades/compiler-core.md` §2–§3.

---

## Sequence

| # | Track | Item | Doc | Depends | Size |
|---|---|---|---|---|---|
| 1 ✅ | upgrades | Compiler core (structure · energy · terminals · ops) | `upgrades/compiler-core.md` | — | M |
| 2 ✅ | upgrades | Op-flow (ordered op list — port on top of the core) | `upgrades/op-flow.md` | 1 | S–M |
| 3 | upgrades | Lattice UI + template editor | `upgrades/lattice-ui.md` | 2 | L |
| 4 | combat | First primitive op behaviors | `combat/primitives.md` | 2 | M |
| 5 | combat | Enemy R + paths / roads / deviation | `combat/enemy-r.md` | 4 | XL |
| 6 | — | Delivery shapes · impact cap · investment axes | *(later)* | 4 | — |

Size: S/M/L/XL rough effort. **Compiler core is item 1** — the engine (structural passes,
local-toll energy routing, auto crystal terminals, combo-op naming at leaf outs). **Op-flow is
item 2** — the ordered op list (produce + collect + order by lattice position) on top; no
consumption here. Both were validated in the playground first, which is the reference.
**Item 3 is next**: the compiler is trustworthy headless, so the lattice UI can render it.

---

## Ordering rationale

- **1 (compiler core) first.** The engine — structural passes, local-toll energy routing,
  auto crystal terminals, combo-op naming — is the foundation everything reads. Built and
  tested headless with the ordered shot stubbed. Op-flow can't produce multipliers without this
  energy engine.
- **2 (op-flow) next.** Layer the ordered op list (produce + collect + order by lattice
  position; no consumption) onto the core. Thin and low-risk: the playground `compile()` is the
  reference to match. This fixes what each shot *carries*. (It was validated in the playground
  first — that is why it exists there ahead of the C# core; the C# build reverses the order.)
- **3 (UI) after the compiler is trustworthy.** The UI's live preview *is* the core compiler
  (+ payload) — render it only once headless tests pass.
- **Then combat, 4 → 5.**
  - **4 (primitives)** is where compilation first *does something* visible in-game — it wires
    the op names flowing out of items 1–2 to real enemy effects.
  - **5 (enemy R)** is the largest scope: it needs the R meter **and** preset enemy paths
    **and** player-placed roads **and** deviation, integrated with tower placement. Last.

---

## Back-port TODO (design docs to reconcile once these land)

- ✅ **Terminal rules** (auto leaf-input / leaf-output, crystal-level sources/sinks, equal
  split, no weights) → canonical in `upgrades/compiler-core.md` §2–§3; back-ported to
  `../compilation-system.md` §2 + `../legend.md`; auto-derived in the playground.
- ✅ **Op flow** → the compiled shot is an **ordered op list** (`../compilation-system.md` §3,
  `upgrades/op-flow.md`); consumption happens on the enemy at hit time, not in the compiler.