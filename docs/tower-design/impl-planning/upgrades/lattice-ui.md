# Lattice UI + Template Editor

The player-facing (and dev-facing) surface for **building and examining** a crystal
lattice — the in-game successor to `../../playground/dataflow-playground.html`. Roadmap item **2**.
Depends on the compiler core (its live preview *is* the core compiler).

Two surfaces share one renderer and one compiler:

1. **In-game builder** — the player edits a tower's lattice during play.
2. **Template editor** — a design/dev tool for authoring lattice *shapes* and default
   crystal configs that tower types load as their starting layout. (Tower types are out of
   scope; the editor that produces their defaults is in scope.)

---

## 1. What differs from the playground

The playground assumed a fixed, fully-uniform grid where every cell is placeable. The real
lattice is not:

- **Size is not static** — lattices grow (investment axis: buy cells).
- **Not every cell is usable** — some grid slots are permanently blocked.
- **Uneven perimeter** — the usable region is a **contour**, not a rectangle; non-uniform
  shape.

So the UI must render an **arbitrary set of triangular cells** (a mask over the grid), not a
filled block. The playground's algorithm ports; its grid assumptions do not.

---

## 2. Renderer

- Triangular-grid draw: cells as up/down triangles, one crystal glyph each, edges as the
  interaction sites. Coordinate scheme TBD (axial + up/down parity is the working proposal).
- **Live compile overlay**, mirroring the playground trace: per-edge energy, combo op +
  multiplier, weapon energy, sinks, debt, and **illegal-build flags**.
- Same `CompileResult` the runtime fires — the preview is truthful by construction.

---

## 3. In-game builder — interactions

- Place / remove a crystal in a **usable, empty** cell; blocked cells reject placement.
- Set a cell's role by geometry (▲ up = split, ▽ down = merge) — orientation is the slot's,
  not a toggle.
- Toggle **sites** (source weight / sink) on open edges. **Sinks only on leaf edges** — the
  UI forbids a sink where the cell still feeds another crystal (`op-flow.md`).
- Show what the current build fires before committing; block or warn on illegal / over-budget
  builds (impact-count cap is a later axis).

---

## 4. Template editor — extra powers

Everything the builder does, plus **authoring the mask itself**:

- Paint which grid slots are **usable / blocked**; sculpt the perimeter contour.
- Save a `{ mask, default crystals, default sites }` bundle as a **template** (a Resource).
- Tower types (later, out of scope) reference a template as their default configuration.
- Round-trips with the compiler for validation, so a shipped template is always legal.

---

## 5. Reuse

- Compiler: `../upgrades/compiler-core.md` (unchanged — UI feeds it a `Lattice`).
- Placement/footprint patterns from `TowerPlacementManager` for the in-world side.
- Template as a `[GlobalClass]` Resource, edited in the tool, loaded at tower spawn.
