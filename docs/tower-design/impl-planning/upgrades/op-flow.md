# Op-Metadata Flow

How op results move through the lattice after a combo produces them. **Roadmap item 2** —
the payload pass, layered on the compiler core (**item 1**, `compiler-core.md`), which
stubs it. This is a **compilation-system** concern, not combat: the payload is part of what
the compiler *emits*. Nothing here makes an op *do* anything to an enemy — that is the
combat track (`../combat/primitives.md`). Already validated in the playground — port that.

Design goal: **keep it simple.** Op payloads ride the same routing energy already uses.

Two rules first stated here — flagged for back-port to `../../compilation-system.md` and
`../../legend.md` (see §5).

---

## 1. Scope (deliberately thin)

This item does **not** implement any op. It only computes, for each leaf-node output, the
**correct set of ops with the correct energy multipliers** — including the effect of
consumption. Concretely:

- **Op names only.** An op is an `OpId` + a float quantity. No behavior, no damage, no
  states written on an enemy.
- **Producers / consumers come from the effect vocabulary**, not from new tables here:
  - which combo produces which op → `../../effect-vocab/vocab-overview/combo-matrix.md`;
  - which interactive op **consumes** which primitive → the op file's **`Consumes:`** header
    (`../../effect-vocab/ops/interactives/`). Only genuine charge-and-spend consumers count —
    ops that merely *read* enemy metadata at runtime (Focus, the Arcs, Accelerant, Numb, …)
    are **not** payload consumers. (README's *Reacts to* column conflates the two.)
- **1-to-1 conversion driver.** When a consumer eats a primitive it emits its product at
  the **same quantity** (1 unit in → 1 unit out). No per-op conversion constants yet —
  those are authored later with the ops (`../combat/primitives.md` / item 4).

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

## 3. Terminal rules (leaf-input / leaf-output)

**Sources and sinks are crystal-level *terminals*, not edge sites.** A boundary crystal is
either seeded by the core (a **source**) or drains to the weapon (a **sink**); interior
crystals are pure crystal↔crystal. Two symmetric constraints (reading A):

- **Leaf-output.** A crystal's outputs are **all-crystal** or **one sink** — never mixed.
  Forbidden: a crystal feeding a downstream crystal *and* a sink.
- **Leaf-input (NEW).** A crystal's inputs are **all-crystal** or **one source** — never
  mixed, never two sources. Forbidden: a crystal fed by a source *and* an upstream crystal,
  or by two sources.
- A **lone** crystal (no neighbours) may be both a source and a sink — the minimal tower.

Under this model a **source crystal is seeded `E_core` directly** (its input arity is
ignored) and a **sink crystal delivers its post-toll energy to the weapon**. The old
per-edge site is gone: a terminal binds to the whole crystal.

- Rationale: a terminal is a *terminus/origin*. Mixing "keep computing" with "emit now" (or
  "seed here") on one cell makes payload accounting ambiguous (what's consumed downstream vs
  already fired/seeded) and lets a build double-dip. Forcing terminals to leaves keeps each
  stream's fate singular at both ends.
- Enforcement: **compiler legality pass** (`compiler-core.md` §3) — a source with any crystal
  input, or a sink with any crystal output, ⇒ `legal = false`. The UI can also enforce it
  *structurally* by only offering a terminal where the crystal has a free side, and pruning
  terminals when a newly placed crystal removes that freedom (`lattice-ui.md` §3). The
  playground already does this.

---

## 4. Data shape (proposed, simple)

- `Payload = Dictionary<OpId, float>` carried per edge, exactly parallel to `energyByEdge`.
- Split: `q / nOut` per output. Merge: element-wise sum.
- A consumer op's `{ consumes: OpId, produces: OpId }` is **read from the vocabulary**
  (README *Reacts to* + combo-matrix), not declared here. At its edge it removes up to the
  available consumed quantity and adds the product at the **same** quantity (1-to-1 driver).
- Unconsumed primitives simply arrive at the output as-is.

Exact consume math (partial consumption? ratios? multiplier scaling?) replaces the 1-to-1
driver per interactive op when those ops are authored (item 4), in
`../../effect-vocab/ops/interactives/`.

---

## 5. Back-port TODO

- ✅ `../../compilation-system.md` §3 — payload channel added (energy is not the only thing
  that flows).
- ✅ `../../compilation-system.md` §2 + `../../legend.md` — terminal rules (leaf-input /
  leaf-output) + crystal-level source/sink stated.
- ✅ Playground — sources/sinks are crystal terminals; both leaf rules enforced structurally.
