# Compiler Core (headless C#)

The engine-free heart of the system: a lattice → weapon-and-ops compiler, a direct port
of `../../playground/archive/dataflow-playground.html` `compile()`. No `using Godot`. Godot nodes and the UI
call in; it never calls back. Testable in isolation and portable.

Reference rules: `../../compilation-system.md`, `../../energy-conservation.md`.
Roadmap item **1** — the engine (structure, energy, terminals, combo-op naming) with the
**ordered shot stubbed**. The op-flow pass (produce + collect + order) is **item 2**
(`op-flow.md`), layered on top. Depends on nothing. The playground `compile()` already does
both — port the engine here first.

**Status: built.** `scripts/towers/crystal/core/` + `tests/CrystalCore.Tests/` (21 tests green).
The test project compiles the core sources directly *without* `Godot.NET.Sdk` — building at all
is the proof it stayed engine-free. Not yet wired to a tower (§5 is still open). The playground
is now archived (`../../playground/archive/README.md` lists where it diverges).

---

## 1. Layout

```
scripts/towers/crystal/core/      ← engine-agnostic
  CrystalKind.cs     enum Ruby, Sapphire, Emerald, Citrine, Amethyst, Quartz
  OpId.cs            enum of the 21 op names (natives + interactives)
  ComboMatrix.cs     static ComboOp(a,b) / NativeOp(k) — mirrors combo-matrix.md
  CrystalStats.cs    ICostTable + cost per kind (Ru 28, Sa 16, Em 22, Ci 12, Am 20, Qz 6);
                     injectable so tests can use the abstract 1/2/3 worked examples
  Lattice.cs         placed cells on (row, col) — the DAG input. Orientation, edges, edge roles,
                     flow order and terminals are all DERIVED, never set by the caller
  Compiler.cs        the passes → CompileResult
  CompileResult.cs   weaponEnergy, edgeOps[], shot (ordered op list), sinks[], trace, legal
```

`CrystalDef` (a `[GlobalClass]` Resource: kind, color, element, texture) and everything
else Godot lives *outside* `core/`.

---

## 2. Data model (graph only)

No floating geometry in core. The playground keyed edges off *rounded pixel coordinates*
(`ek()`); the core replaces that with integer lattice coordinates and an unordered cell-id pair.

- **Cell** `{ int id, CellCoord coord, CrystalKind kind }` where `CellCoord = (row, col)`.
  **`row` grows UPWARD** (row 0 is the bottom) — the same direction energy flows, so growing a
  lattice taller never needs negative rows. **Orientation is derived, not stored**:
  `Up ⟺ (row + col) even`. `Orientation ∈ {Up, Down}` = split / merge arity, and the bipartite
  rule (Up only ever neighbors Down) then holds by construction.
- **A row is one horizontal BAND** of the tiling and holds **both** orientations, interlocked
  side by side — a ▲ stands on the band's lower line, a ▽ hangs from its upper line. They are
  not on separate rows; within a band the ▲ sits *below* the ▽s beside it (that half-level is
  what `FlowDepth` in §3 encodes). `col` indexes left→right across the band, so orientation
  alternates every step.
- **Adjacency is derived too** — the caller supplies only *which slots are filled*:

  | | in-side (feeds it) | out-side (it feeds) |
  |---|---|---|
  | ▲ `(r,c)` | `(r-1, c)` | `(r, c-1)`, `(r, c+1)` |
  | ▽ `(r,c)` | `(r, c-1)`, `(r, c+1)` | `(r+1, c)` |

  One cell's out-edge is always its neighbor's in-edge, so ▲ = 1-in/2-out and ▽ = 2-in/1-out
  fall out of the coordinates. Note flow takes **two half-steps per band**: a ▲ feeds the ▽s in
  its *own* row, and a ▽ feeds the ▲ one row up. A slot that is empty *or* off-mask is an
  **open** side — the core does not distinguish them.
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
  contour, and size is not fixed (see `lattice-ui.md`). The core only sees the cells that exist
  — shape is the UI's problem, compiled down to a set of `(row, col, kind)` placements.

---

## 3. Passes (port of `compile()`)

Structural first, then values, then effects.

**Sweep order.** "Bottom→top" is **not** just row-ascending: a ▲ feeds the ▽s *in its own row*
(they only pass energy up a row from their own tops). The topological key is
`FlowDepth = 2·row + (▲ ? 0 : 1)`, swept **ascending** — ▲(r), ▽(r), ▲(r+1), ▽(r+1), … It rises
with height, so it is also the "lowest gem first" key for the ordered shot (`op-flow.md` §3).
The playground sorts gem centroids by `cy` descending; that is the same order, because its
screen `y` grows downward where our `row` grows upward.

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
7. **Collect**: `weaponEnergy = Σ max(0, sinkEnergy)`.

   **Energy cannot leak.** Crystal cost is the only thing that removes energy, so
   `weaponEnergy = E_core − Σ cost` always. Two structural facts guarantee it: following
   out-edges from any crystal must end at a leaf output, and every leaf output *is* a sink
   (auto-terminals); and pass 2 runs before pass 4, writing energy only onto edges whose
   downstream node is productive, so **fed ⇒ productive** and no branch can dead-end.

   There is deliberately **no `lostEnergy` counter**. It existed for manual terminals, where a
   chain could be built without a sink on top ("you forgot a sink"); under auto-terminals that
   is unbuildable, so a non-zero value would only ever mean a compiler bug, not a player
   mistake. The conservation identity above is asserted in the tests instead.

---

## 4. Acceptance tests (lock the port to the docs)

Encoded as xUnit in `tests/CrystalCore.Tests/` (all green except the item-2 one, which asserts
the stub is empty):

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
outputs match the playground for shared inputs (shot aside). ✅ done — see the status note above.

---

## 5. Integration seam

- `CrystalTower : StaticBody2D, ITowerPlaceable` holds a `Lattice`, calls `Compiler`, caches
  `CompileResult`. Reuses existing placement/footprint/destroy machinery
  (`ITowerPlaceable.Configure/Destroy`, `TowerPlacementManager`).
- Recompile on lattice edit (cheap, finite DAG). Fire loop can start from the current
  `TurretTower` cadence; **each shot applies the cached result** to the hit enemy.
- HP damage via existing `HealthComponent.TakeDamage`; R meter is new (`../combat/enemy-r.md`).