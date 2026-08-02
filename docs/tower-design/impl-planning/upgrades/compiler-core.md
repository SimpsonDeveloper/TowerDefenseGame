# Compiler Core (headless C#)

The engine-free heart of the system: a lattice → weapon-and-ops compiler, a direct port
of `../../playground/dataflow-playground.html` `compile()`. No `using Godot`. Godot nodes and the UI
call in; it never calls back. Testable in isolation and portable.

Reference rules: `../../compilation-system.md`, `../../energy-conservation.md`.
Roadmap item **1**. Depends on nothing.

---

## 1. Layout

```
scripts/towers/crystal/core/      ← engine-agnostic
  CrystalKind.cs     enum Ruby, Sapphire, Emerald, Citrine, Amethyst, Quartz
  OpId.cs            enum of the 21 op names (natives + interactives)
  ComboMatrix.cs     static ComboOp(a,b) / NativeOp(k) — mirrors combo-matrix.md
  CrystalStats.cs    cost per kind (Ru 28, Sa 16, Em 22, Ci 12, Am 20, Qz 6)
  Lattice.cs         cells, edges, orientation, sources, sinks — the DAG input
  Compiler.cs        the passes → CompileResult
  CompileResult.cs   weaponEnergy, edgeOps[], payload, sinks[], lostEnergy, trace, legal
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
- **Site** = an edge flagged **Source** (with weight) or **Sink**.
- The lattice is **non-uniform**: not every grid slot is a usable cell, the perimeter is a
  contour, and size is not fixed (see `lattice-ui.md`). The core only sees the cells/edges
  that exist — shape is the UI's problem, compiled down to `(id, kind, orient, edges, sites)`.

---

## 3. Passes (port of `compile()`)

Structural first, then values, then effects.

1. **Productivity** (top→bottom): mark cells/edges that can reach a sink.
2. **Fed** (bottom→top): mark cells reachable from a source. `active = productive ∧ fed`.
3. **Legality** (structural): enforce the **leaf-node output** rule — a cell with a sink
   has no other productive output. Illegal builds set `legal = false` (see `../combat/op-flow.md`).
4. **Energy routing** (bottom→top): seed each source `E_core × weight`; per cell
   `outE = inSum − cost` (local toll, may go negative = **debt**); Up divides `outE` across
   outputs, Down sums inputs. Debt flows unclamped so a later merge can recover it.
5. **Op production**: on each active edge, `(upKind, downKind) → ComboOp`, scaled by
   `max(0, energyAtDownstream)`. Emit `EdgeOp { op, energy, debt }`.
6. **Payload flow** (roadmap item 3, `../combat/op-flow.md`): op quantities propagate and
   are consumed along the same routing. Stubbed until then.
7. **Collect**: `weaponEnergy = Σ max(0, sinkEnergy)`; usable energy dead-ending in a
   sinkless branch = `lostEnergy`.

---

## 4. Acceptance tests (lock the port to the docs)

Encode the `../../energy-conservation.md` worked example as xUnit:

- `E_core = 20`, chain costs `1,2,3` → energy-in `20 / 19 / 17`, exit **14**.
- Split `a → {b,c}` → `9.5` each → tolled to `7.5 / 6.5`.
- Debt: a chain whose tolls exceed `E_core` floors combo output at 0 but keeps the negative
  in transit; a downstream merge recovers it.
- Legality: a cell feeding one crystal **and** a sink → `legal = false`.

The core is "done" when these pass and outputs match the playground for shared inputs.

---

## 5. Integration seam

- `CrystalTower : StaticBody2D, ITowerPlaceable` holds a `Lattice`, calls `Compiler`, caches
  `CompileResult`. Reuses existing placement/footprint/destroy machinery
  (`ITowerPlaceable.Configure/Destroy`, `TowerPlacementManager`).
- Recompile on lattice edit (cheap, finite DAG). Fire loop can start from the current
  `TurretTower` cadence; **each shot applies the cached result** to the hit enemy.
- HP damage via existing `HealthComponent.TakeDamage`; R meter is new (`../combat/enemy-r.md`).