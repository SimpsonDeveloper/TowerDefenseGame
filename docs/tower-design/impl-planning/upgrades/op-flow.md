# Op-Metadata Flow

How op results move through the lattice after a combo produces them. **Roadmap item 1**
(with the compiler core — `compiler-core.md`). This is a **compilation-system** concern,
not combat: the payload is part of what the compiler *emits*. Nothing here makes an op
*do* anything to an enemy — that is the combat track (`../combat/primitives.md`).

Design goal: **keep it simple.** Op payloads ride the same routing energy already uses.

Two rules first stated here — flagged for back-port to `../../compilation-system.md` and
`../../legend.md` (see §5).

---

## 1. Scope of item 1 (deliberately thin)

The first milestone does **not** implement any op. It only computes, for each leaf-node
output, the **correct set of ops with the correct energy multipliers** — including the
effect of consumption. Concretely:

- **Op names only.** An op is an `OpId` + a float quantity. No behavior, no damage, no
  states written on an enemy.
- **Producers / consumers come from the effect vocabulary**, not from new tables here:
  - which combo produces which op → `../../effect-vocab/vocab-overview/combo-matrix.md`;
  - which interactive op **consumes** which primitive → the *Reacts to* column of
    `../../effect-vocab/ops/README.md` (each interactive's own file states it too).
- **1-to-1 conversion driver.** When a consumer eats a primitive it emits its product at
  the **same quantity** (1 unit in → 1 unit out). No per-op conversion constants yet —
  those are authored later with the ops (`../combat/primitives.md` / item 3).

Done when: a lattice compiles to a leaf-out payload whose `(OpId → quantity)` entries match
the hand-worked example below.

---

## 2. The model

Energy is not the only thing that flows. Alongside it flows a **payload** — a bag of
`(OpId → quantity)`.

1. **Produce.** Each active edge's combo produces its op with a **multiplier = energy
   arriving at the downstream crystal** (floored at 0, per `../../energy-conservation.md`).
   That becomes a quantity added to the stream's payload.
2. **Propagate.** The payload floats **up along the energy flow**: a **split (▲) divides**
   each op quantity across outputs; a **merge (▽) sums** the payloads of its inputs. Same
   split/merge the lattice already defines for energy.
3. **Consume.** An **interactive** op consumes the upstream **primitive** it reacts to and
   emits its product. Consumption happens where the interactive combo fires: it subtracts
   the consumed quantity and adds the product (1-to-1 for now).
4. **Terminate.** Propagation + consumption continue until the stream reaches an **output**
   (a leaf sink). The payload there is what the shot carries.

### Worked example

Split **10 burn** → two branches of **5**. One branch's 5 is consumed by Frostburn (it
reacts to Burn) → **5 frostburn**. The branches **merge**: the merge sums to
**5 burn + 5 frostburn**. Continue propagating / consuming toward the output.

Implication: a primitive can pass through untouched on one path while being transformed on
another; merges recombine what survived with what was produced.

---

## 3. Leaf-node output rule (NEW)

**Outputs (sinks) must sit at leaf nodes.** A cell may **either** feed downstream crystals
**or** terminate at an output — never both.

- Forbidden: a crystal splitting 50% of its output into another crystal and 50% into a
  lattice-edge sink.
- Rationale: an output is a *terminus*. Mixing "keep computing" and "emit now" on one cell
  makes payload accounting ambiguous (what's consumed downstream vs already fired) and lets a
  build double-dip. Forcing outputs to leaves keeps each stream's fate singular.
- Enforcement: **compiler legality pass** (`compiler-core.md` §3) — a cell with a sink and
  any other productive output ⇒ `legal = false`. The UI blocks it at author time
  (`lattice-ui.md` §3).

---

## 4. Data shape (proposed, simple)

- `Payload = Dictionary<OpId, float>` carried per edge, exactly parallel to `energyByEdge`.
- Split: `q / nOut` per output. Merge: element-wise sum.
- A consumer op's `{ consumes: OpId, produces: OpId }` is **read from the vocabulary**
  (README *Reacts to* + combo-matrix), not declared here. At its edge it removes up to the
  available consumed quantity and adds the product at the **same** quantity (1-to-1 driver).
- Unconsumed primitives simply arrive at the output as-is.

Exact consume math (partial consumption? ratios? multiplier scaling?) replaces the 1-to-1
driver per interactive op when those ops are authored (item 3), in
`../../effect-vocab/ops/interactives/`.

---

## 5. Back-port TODO

- ✅ `../../compilation-system.md` §3 — payload channel added (energy is not the only thing
  that flows).
- `../../compilation-system.md` §2 + `../../legend.md` — state the leaf-node output rule.
- Playground — currently permits sinks on any open edge; update to the leaf rule.
