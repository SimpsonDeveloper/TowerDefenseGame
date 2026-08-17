# Compiler Core (headless C#)

The engine-free heart of the system: a lattice → weapon-and-ops compiler, a direct port
of `../../playground/archive/dataflow-playground.html` `compile()`. No `using Godot`. Godot nodes and the UI
call in; it never calls back. Testable in isolation and portable.

Reference rules: `../../compilation-system.md`, `../../energy-conservation.md`.
Roadmap item **1** — the engine (structure, energy, terminals, combo-op naming). The op-flow
pass (produce + collect + order) is **item 2** (`op-flow.md`), layered on top and now landed.
Depends on nothing. The playground `compile()` already does both — port the engine here first.

**Status: built.** `scripts/towers/crystal/core/` + `tests/CrystalCore.Tests/` (35 tests green).
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
  Cell.cs            Orientation, CellCoord (row, col, Height), Cell — immutable
  Lattice.cs         which slots hold crystals — the whole compiler input. Orientation,
                     adjacency, terminals and walk order are all QUESTIONS YOU ASK IT
  Compiler.cs        three pure steps → CompileResult
  CompileResult.cs   CellEnergy, Terminal, EdgeOp, ShotOp, CompileResult
  LatticeMask.cs     which slots EXIST — the lattice's shape (item 3, `lattice-ui.md` §1)
  LatticeGeometry.cs where a cell sits: corners, centre, and point → cell
  LatticeCamera.cs   fits a lattice into a rectangle; lattice space ↔ view space
  LatticeSnapshot.cs a lattice as plain data — the save format behind a template
```

The last four are item 3's, but they are engine-free lattice knowledge, so they live here and
are unit-tested here. Nothing in `Compiler` reads them.

`CrystalDef` (a `[GlobalClass]` Resource: kind, color, element, texture) and everything
else Godot lives *outside* `core/`.

---

## 2. Data model (graph only)

No floating geometry in core, and no edge objects at all: the playground keyed edges off
*rounded pixel coordinates* (`ek()`), while the core needs only integer `(row, col)` and reads
an edge as "a cell and one of its in-neighbours" wherever it needs one.

- **Cell** `{ int id, CellCoord coord, CrystalKind kind }` where `CellCoord = (row, col)`.
  **`row` grows UPWARD** (row 0 is the bottom) — the same direction energy flows, so growing a
  lattice taller never needs negative rows. **Orientation is derived, not stored**:
  `Up ⟺ (row + col) even`. `Orientation ∈ {Up, Down}` = split / merge arity, and the bipartite
  rule (Up only ever neighbors Down) then holds by construction.
- **A row is one horizontal BAND** of the tiling and holds **both** orientations, interlocked
  side by side — a ▲ stands on the band's lower line, a ▽ hangs from its upper line. They are
  not on separate rows; within a band the ▲ sits *below* the ▽s beside it (that half-level is
  what `Height` in §3 encodes). `col` indexes left→right across the band, so orientation
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
- **Terminal** = a **cell** at the boundary — crystal-level, not an edge site. **Automatic and
  always on; the user never sets them** (req. for the C# impl too). Not a classification pass
  and not stored state, but two **predicates**: `source ⟺ nothing feeds it`,
  `sink ⟺ it feeds nothing`; a lone cell is both. A source is seeded core energy; a sink drains
  to the weapon. Because they *are* the leaf conditions, the leaf-input / leaf-output rules hold
  **by construction** — no illegal terminal is expressible.
- **No input weights** (req. for the C# impl too). The core energy is split **equally** among
  the source cells: each gets `E_core / nSources`. There is no per-source weight.
- The lattice is **non-uniform**: not every grid slot is a usable cell, the perimeter is a
  contour, and size is not fixed (see `lattice-ui.md`). Shape is **authored** by the UI, as a
  `LatticeMask` — a plain set of usable coordinates. A `Lattice` optionally carries one and
  refuses to `Place` off it, so an off-mask crystal is no more expressible than an illegal
  terminal; a lattice with no mask is the unrestricted grid the compiler's own tests use.
  Editing the shape afterwards goes through `Lattice.Allow`/`Block`, which demolishes rather than
  strand a crystal, so the invariant holds for the lattice's whole life and not just at `Place`.
  Compiling reads none of this: it still only sees the `(row, col, kind)` placements.

---

## 3. Steps

Three, each a pure function of its arguments. No shared mutable state, no flags threaded
between them.

**Sweep order.** "Bottom→top" is **not** just row-ascending: a ▲ feeds the ▽s *in its own row*
(they only pass energy up a row from their own tops). The topological key is
`Height = 2·row + (▲ ? 0 : 1)`, swept **ascending** — ▲(r), ▽(r), ▲(r+1), ▽(r+1), … It rises
with height, so it is also the "lowest gem first" key for the ordered shot (`op-flow.md` §3).
The playground sorts gem centroids by `cy` descending; that is the same order, because its
screen `y` grows downward where our `row` grows upward.

**Terminals are not a pass.** `source ⟺ nothing feeds it`, `sink ⟺ it feeds nothing` — two
predicates on the lattice (`Lattice.IsSource` / `IsSink`), evaluated on demand. There is nothing
to derive into a set, nothing to store, and nothing a player could set. That is the strongest
form of "always on, never user-set".

1. **Route energy** (bottom→top, one sweep): seed each source `E_core / nSources` — an **equal**
   split, no weights. Then per cell `out = in − cost` (local toll, may go negative = **debt**),
   where `in` is the **sum of the in-neighbours' per-output shares**. That one sum covers both
   orientations: a ▽ has two inputs and sums them, a ▲ has exactly one, so the sum *is* it. Each
   cell then offers `out / outCount` to each out-neighbour — a ▲ halves, a ▽ passes the lot.
   Debt is never clamped in transit, so a ▽ downstream can sum it back above 0.
2. **Name ops**: for every internal edge, `(upKind, downKind) → ComboOp` at the energy that
   crossed it (the upstream cell's per-output share), floored at 0. Emit `EdgeOp`.
3. **Order the shot** (**item 2**, `op-flow.md`): flatten the `EdgeOp`s into one **ordered list**
   — no bag, no consume, no split/merge of quantities. Each op is anchored to its **downstream**
   cell and sorted `Height` asc → downstream `col` asc → upstream `col` asc → op name. Edges
   carrying no energy produce nothing.

Then assemble: `weaponEnergy = Σ max(0, sinkEnergy)`.

### What is deliberately *not* here

Under auto-terminals these are all identically true, so modelling them would be modelling a
constant:

- **Productive** (can reach a sink) — following out-edges must end at a leaf output, and every
  leaf output *is* a sink. Every cell is productive.
- **Fed** (reachable from a source) — a cell that feeds someone has an out-neighbour, so it is
  not a sink, so it routes; therefore every cell with in-neighbours is fed, and a cell without
  them *is* a source. Every cell is fed.
- **Active edge** = productive ∧ fed ⇒ **every internal edge is active**, and every one fires.

They come back the moment a rule can switch an edge off — conditional branching at a split is
the expected one. The agreed model there is "a cut edge is not live", which drops it out of
`outCount`, so the split hands its whole share to the surviving sibling and nothing is lost. Add
the filter in step 2 when that lands; do not carry a dead pass until then.

**Energy cannot leak.** Crystal cost is the only thing that removes energy, so
`weaponEnergy = E_core − Σ cost` always — the same two facts above are what guarantee it.

There is deliberately **no `lostEnergy` counter**. It existed for manual terminals, where a
chain could be built without a sink on top ("you forgot a sink"); under auto-terminals that is
unbuildable, so a non-zero value would only ever mean a compiler bug, not a player mistake. The
conservation identity is asserted in the tests instead.

---

## 4. Acceptance tests (lock the port to the docs)

Encoded as xUnit in `tests/CrystalCore.Tests/`, all green:

- `E_core = 20`, chain costs `1,2,3` → energy-in `20 / 19 / 17`, exit **14**.
- Split `a → {b,c}` → `9.5` each → tolled to `7.5 / 6.5`.
- Debt: a chain whose tolls exceed `E_core` floors combo output at 0 but keeps the negative
  in transit; a downstream merge recovers it.
- Auto-terminals: in a chain, only the input-leaf cell is a source and only the output-leaf
  cell is a sink; interior cells are neither; a lone cell is both. A cell feeding a crystal is
  **not** a sink; a cell fed by a crystal is **not** a source (leaf rules hold by construction).
- Equal split: two sources, `E_core = 20` → each seeded **10** (no weights).
- Conservation: `weaponEnergy == E_core − Σ cost` across a chain, a split and a merge, at
  several core levels (this is what replaced `lostEnergy`).
- Walk order: every cell's in-neighbours come **before** it in `FlowOrder()`. This is the one
  precondition the routing sweep depends on — the whole reason `Height` counts half-levels.
- Every internal edge fires: op count == internal-edge count (there is no inactive case).
- Ordered shot (**item 2**, `op-flow.md` — `ShotOrderTests.cs`): chain Ruby → Ruby → Sapphire →
  shot = `[Burn ×(E@mid), Frostburn ×(E@top)]` in that order (Burn's gem is lower, so Burn
  first; **no consumption** — both ride the shot).

The core (item 1) is "done" when the energy / structure / auto-terminal tests pass and its
outputs match the playground for shared inputs. ✅ done — see the status note above.

---

## 5. Integration seam

**There is no separate crystal tower.** An earlier draft of this section proposed
`CrystalTower : StaticBody2D` — wrong. The lattice is an **upgrade surface on the towers that
already exist**, exactly as `../roadmap.md`'s ground facts say ("each tower owns its own crystal
lattice"). A tower fires a compiled shot; a tower with no lattice fires as it always did.

- ✅ `TurretTower` holds a `Lattice`, calls `Compiler`, caches the `CompileResult`. `TowerDef`
  gained `Lattice` (a `CrystalTemplate`) and `CoreEnergy`. Existing placement/footprint/destroy
  machinery is untouched — the lattice rides in through the same `ITowerPlaceable.Configure(def)`.
- ✅ A template describes a *starting* lattice; `Configure` builds the tower its own copy, so a
  player's edits never write back to the shipped asset.
- ✅ `Recompile()` after an edit (cheap, finite DAG). Shots use the existing `TurretTower`
  cadence and carry the cached result.
- ✅ **The lattice's whole effect on combat is `ShotLanded`.** The gun's flat `TowerDef.Damage` is
  untouched by it. A tower briefly had a `DamagePerWeaponEnergy` factor folding weapon energy into
  the damage number; that was scope creep and is **removed**. A compiled shot is a list of ops, and
  whether an op does damage is decided by that op's definition — some do, some do not. Deriving
  damage here would double-count the ones that do and invent damage for the ones that don't.
  Forwarding the whole `CompileResult` is the only correct thing this class can do before item 4.
- ✅ Reaching the lattice: `TowerPlacementManager` gained **Editing** beside Placing and
  Destroying — same state machine, same tower-under-cursor lookup as destroy. It raises
  `TowerEditRequested` and stops there, so the manager keeps no dependency on the editor; the UI
  decides what opening one looks like. Today that is `CrystalLatticeEditor` full-screen with the
  tree **paused**, because reading a compile trace while a wave advances would make the tool a
  liability and the tower would fire a stale shot meanwhile.
- ⚠️ **The editor is not under `TowerPlacementUI`, and cannot be.** That UI lives inside the pocket
  dimension's `SubViewport`, and a `SubViewport` gets input only because the `SubViewportContainer`
  above it forwards it — which that container can only do while it is processing input. Pausing the
  tree stops it, so anything hosted in there goes **deaf the moment it pauses the game**, no matter
  what `ProcessMode` it sets on itself: `Always` decides whether an event is *handled*, and here
  none *arrives*. A modal that outlives a pause has to sit on the viewport that reads the mouse.

  So `main_scene_raycast_agent.tscn` states the split outright, and the editor lives on the far
  side of it:

  ```
  Node2D
  ├─ WhenPaused    process_mode = 2   ← runs only while paused
  │   └─ LatticeEditorLayer  CanvasLayer
  │       └─ CrystalLatticeEditor
  └─ Pausable      process_mode = 1   ← the game and its HUD
      ├─ WorldManager · OverworldContainer · PocketDimensionContainer
      └─ SpawnFadeOverlay · WaveTimerOverlay
  ```

  Anything else that must survive a pause parents under `WhenPaused` and inherits the behaviour
  without knowing this rule exists, which is the point of naming the branches.

  `TowerPlacementUI` reaches the editor through an exported `NodePath` and only drives it. That
  export briefly could not work: the pocket dimension used to be its own scene, so the slot was
  unfillable from the scene that owned the node and settable only as a parent-scene override. It
  is now inlined — `pocket_dimension.tscn` had exactly one reference and nothing loaded it at
  runtime — so both ends of the wire live in one file and the export is assignable by drag.

  Draw order is `scripts/ui/UiLayer.cs`: **0–99 pausable, 100+ modal**, every value assigned in
  `_Ready` so the scene never carries a competing number. Godot has no sublayers — `layer` is one
  absolute int per viewport and nesting `CanvasLayer`s does not compose — so a stated convention is
  the only mechanism there is. The band is also what makes "full-screen" true: before it, the other
  dimension's mini-view and the wave timer drew on top of the editor.
- **Open:** the R meter (`../combat/enemy-r.md`), and item 4 consuming the ordered ops.