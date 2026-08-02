# Compiler Core (headless C#)

The engine-free heart of the system: a lattice → weapon-and-ops compiler, a direct port
of `../../playground/dataflow-playground.html` `compile()`. No `using Godot`. Godot nodes and the UI
call in; it never calls back. Testable in isolation and portable.

Reference rules: `../../compilation-system.md`, `../../energy-conservation.md`.
Roadmap item **1** — the engine (structure, energy, terminals, combo-op naming) with the
**ordered shot stubbed**. The op-flow pass (produce + collect + order) is **item 2**
(`op-flow.md`), layered on top. Depends on nothing. The playground `compile()` already does
both — port the engine here first.

---

## 1. Layout

```
scripts/towers/crystal/core/      ← engine-agnostic
  CrystalKind.cs     enum Ruby, Sapphire, Emerald, Citrine, Amethyst, Quartz
  OpId.cs            enum of the 21 op names (natives + interactives)
  ComboMatrix.cs     static ComboOp(a,b) / NativeOp(k) — mirrors combo-matrix.md
  CrystalStats.cs    cost per kind (Ru 28, Sa 16, Em 22, Ci 12, Am 20, Qz 6)
  Lattice.cs         cells, edges, orientation — the DAG input (terminals are derived, not set)
  Compiler.cs        the passes → CompileResult
  CompileResult.cs   weaponEnergy, edgeOps[], shot (ordered op list), sinks[], lostEnergy, trace, legal
```

`CrystalDef` (a `[GlobalClass]` Resource: kind, color, element, texture) and everything
else Godot lives *outside* `core/`.

---

## 2. Data model (graph only)

The playground keys off cell indices + an edge key `ek(a,b)` (unordered pair). Port that;
no floating geometry in core.

- **Cell** `{ int id, CrystalKind kind, Orientation orient }`. `Orientation ∈ {Up, Down}`
  = split / merge arity. Bipartite: Up only ever neighbors Down.
- **Edge** = unordered `(cellA, cellB)`, canonical key `min,max` (like `ek()`).
- **Terminal** = a **cell** the compiler **auto-classifies** as **Source** and/or **Sink** —
  crystal-level, not an edge site. **Automatic and always on; the user never sets them** (req.
  for the C# impl too). A cell is a **source iff it is a leaf on its input side** (no crystal
  feeds it) and a **sink iff it is a leaf on its output side** (no crystal sits above it); a
  lone cell is both. A source cell is seeded core energy; a sink cell drains to the weapon.
  Because terminals are *derived* at leaf sides, the leaf-input / leaf-output rules hold **by
  construction** — no illegal terminal can exist.
- **No input weights** (req. for the C# impl too). The core energy is split **equally** among
  the source cells: each gets `E_core / nSources`. There is no per-source weight.
- The lattice is **non-uniform**: not every grid slot is a usable cell, the perimeter is a
  contour, and size is not fixed (see `lattice-ui.md`). The core only sees the cells/edges
  that exist — shape is the UI's problem, compiled down to `(id, kind, orient, edges, terminals)`.

---

## 3. Passes (port of `compile()`)

Structural first, then values, then effects.

1. **Terminals** (auto-derive): for each cell, `source = leaf on input side`,
   `sink = leaf on output side` (a lone cell is both). **Always on, never user-set, no weights**
   (§2). The leaf-input / leaf-output rules therefore hold **by construction** — there is no
   separate legality check for them, and `legal` stays reserved for other concerns (e.g. impact
   bounding).
2. **Productivity** (top→bottom): mark cells/edges that can reach a sink.
3. **Fed** (bottom→top): mark cells reachable from a source. `active = productive ∧ fed`.
4. **Energy routing** (bottom→top): **seed each source cell** `E_core / nSources` — an **equal**
   split, no weights — directly (input arity ignored); per cell `outE = inSum − cost` (local
   toll, may go negative = **debt**); Up divides `outE` across outputs, Down sums inputs; a
   **sink cell** delivers `outE` to the weapon instead of routing onward. Debt flows unclamped so
   a later merge can recover it.
5. **Op production**: on each active edge, `(upKind, downKind) → ComboOp`, scaled by
   `max(0, energyAtDownstream)`. Emit `EdgeOp { op, energy, debt }`.
6. **Ordered shot** — **stubbed in item 1**; the collect-and-order pass is **item 2**
   (`op-flow.md`). It flattens the active `EdgeOp`s into one **ordered list** (no bag, no
   consume, no split/merge of quantities) sorted by lattice position. The core exposes the seam
   (an empty `shot` on `CompileResult`) and leaves it empty until then.
7. **Collect**: `weaponEnergy = Σ max(0, sinkEnergy)`; usable energy dead-ending in a
   sinkless branch = `lostEnergy`.

---

## 4. Acceptance tests (lock the port to the docs)

Encode the `../../energy-conservation.md` worked example as xUnit:

- `E_core = 20`, chain costs `1,2,3` → energy-in `20 / 19 / 17`, exit **14**.
- Split `a → {b,c}` → `9.5` each → tolled to `7.5 / 6.5`.
- Debt: a chain whose tolls exceed `E_core` floors combo output at 0 but keeps the negative
  in transit; a downstream merge recovers it.
- Auto-terminals: in a chain, only the input-leaf cell is a source and only the output-leaf
  cell is a sink; interior cells are neither; a lone cell is both. A cell feeding a crystal is
  **not** a sink; a cell fed by a crystal is **not** a source (leaf rules hold by construction).
- Equal split: two sources, `E_core = 20` → each seeded **10** (no weights).
- Ordered shot (**item 2**, `op-flow.md`): chain Ruby → Ruby → Sapphire → shot =
  `[Burn ×(E@mid), Frostburn ×(E@top)]` in that order (Burn's gem is lower, so Burn first;
  **no consumption** — both ride the shot). This test lands with op-flow, not the core.

The core (item 1) is "done" when the energy / structure / auto-terminal tests pass and its
outputs match the playground for shared inputs (shot aside).

---

## 5. Integration seam

- `CrystalTower : StaticBody2D, ITowerPlaceable` holds a `Lattice`, calls `Compiler`, caches
  `CompileResult`. Reuses existing placement/footprint/destroy machinery
  (`ITowerPlaceable.Configure/Destroy`, `TowerPlacementManager`).
- Recompile on lattice edit (cheap, finite DAG). Fire loop can start from the current
  `TurretTower` cadence; **each shot applies the cached result** to the hit enemy.
- HP damage via existing `HealthComponent.TakeDamage`; R meter is new (`../combat/enemy-r.md`).