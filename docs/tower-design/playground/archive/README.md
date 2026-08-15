# Playground (archived reference)

The browser prototype the compiler was designed in. **Superseded** by the headless C# core at
`scripts/towers/crystal/core/` (roadmap item 1, `../../impl-planning/upgrades/compiler-core.md`)
— that is now the implementation of record.

Archived, not deleted: it is still the only way to *see* a lattice compile, and its `compile()`
remains a readable statement of the routing model. Keep referencing it for **behavior**; do not
copy its **representation**.

- `dataflow-playground.html` — the lattice explorer (`compile()` lives here)
- `operator-reference.html` — combo-matrix browser
- `crystal-core.js` — roster + combo matrix, shared by both pages

Open the HTML over `http://` (e.g. `python -m http.server`), not `file://` — the pages load
`crystal-core.js` as a classic script.

---

## Still faithful — safe to read as the spec

- **Auto terminals.** Source ⟺ leaf on the input side, sink ⟺ leaf on the output side, a lone
  crystal is both. Derived every compile, never user-set.
- **Equal split, no weights.** Each of `n` sources is seeded `E_core / n`.
- **Local toll.** `out = in − cost` at every crystal.
- **▲ divides / ▽ sums.** A ▲ divides its post-toll energy among its **productive** outputs only
  (dead branches are excluded from the divisor); a ▽ sums its inputs, **debt included and
  unclamped**, so a merge can recover a negative branch.
- **Combo op magnitude** = energy arriving at the **downstream** crystal, floored at 0.
- **Weapon energy** = Σ max(0, sink energy).

## Diverges from the C# core — do not copy

| | archived playground | `scripts/towers/crystal/core/` |
|---|---|---|
| **Vertical direction** | screen `y` grows **downward**; row 0 is the top | `row` grows **upward**, with the flow; row 0 is the bottom |
| **Flow / order key** | gem centroid `cy` **descending** | `FlowDepth = 2·row + (▲ ? 0 : 1)` **ascending** |
| **Coordinates** | real pixel geometry — triangle vertices, centroids, edges keyed by rounded coordinates (`ek()`); edge role read off "is this edge horizontal" | no geometry at all — integer `(row, col)`, orientation = parity of `row+col`, adjacency from a fixed ±1 table, edge key = unordered cell-id pair |
| **Grid shape** | uniform: every triangle that fits the canvas | non-uniform mask with an uneven perimeter (`../../impl-planning/upgrades/lattice-ui.md` §1) |
| **`lostEnergy`** | still computed, with an "Energy dead-ends" verdict | **removed** — unreachable under auto-terminals; the core asserts `weaponEnergy == E_core − Σcost` instead |
| **Ordered shot** | fully implemented (`orderedOps`) | **stubbed** (empty `Shot`) — lands with op-flow, roadmap item 2 |
| **Crystal cost** | hardcoded per kind in `CRYSTALS` | injectable `ICostTable`; `CrystalStats.Default` ships the same numbers |
| **Op identity** | display strings (`'Chill → Freeze'`) | `OpId` enum, with `Ops.Display()` for the doc-facing name |

The two vertical rows of that table describe the **same ordering** — the playground's `cy`
descending and the core's `FlowDepth` ascending produce identical op sequences, because screen
`y` and lattice `row` run opposite ways. Only the arithmetic differs.
