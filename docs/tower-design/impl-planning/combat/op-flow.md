# Op-Metadata Flow

How op results move through the lattice after a combo produces them. Roadmap item **3**;
foundational for all combat. Two rules first stated here — flag for back-port to
`../../compilation-system.md` and `../../legend.md`.

Design goal: **keep it simple.** Op payloads ride the same routing energy already uses.

---

## 1. The model

Energy is not the only thing that flows. Alongside it flows a **payload** — a bag of
`(OpId → quantity)`.

1. **Produce.** Each active edge's combo produces its op with a **multiplier = energy
   arriving at the downstream crystal** (floored at 0, per `../../energy-conservation.md`).
   That becomes a quantity added to the stream's payload.
2. **Propagate.** The payload floats **up along the energy flow**: a **split (▲) divides**
   each op quantity across outputs; a **merge (▽) sums** the payloads of its inputs. Same
   split/merge the lattice already defines for energy.
3. **Consume.** An **interactive** op consumes upstream **primitive** quantities and emits
   a new op. Consumption happens where the interactive combo fires; it subtracts the inputs
   it eats and adds its product to the payload.
4. **Terminate.** Propagation + consumption continue until the stream reaches an **output**.
   The payload at the output is what the shot carries.

### Worked example (user's)

Split **10 burn** → two branches of **5**. One branch's 5 is consumed by an interactive →
**frostburn**. The branches **merge**: the merge sums to **5 burn + (frostburn produced)**.
Continue propagating / consuming toward the output.

Implication: a primitive can pass through untouched on one path while being transformed on
another; merges recombine what survived with what was produced.

---

## 2. Leaf-node output rule (NEW)

**Outputs (sinks) must sit at leaf nodes.** A cell may **either** feed downstream crystals
**or** terminate at an output — never both.

- Forbidden: a crystal splitting 50% of its output into another crystal and 50% into a
  lattice-edge sink.
- Rationale: an output is a *terminus*. Mixing "keep computing" and "emit now" on one cell
  makes payload accounting ambiguous (what's consumed downstream vs already fired) and lets a
  build double-dip. Forcing outputs to leaves keeps each stream's fate singular.
- Enforcement: **compiler legality pass** (`../upgrades/compiler-core.md` §3) — a cell with a
  sink and any other productive output ⇒ `legal = false`. The UI blocks it at author time
  (`../upgrades/lattice-ui.md` §3).

---

## 3. Data shape (proposed, simple)

- `Payload = Dictionary<OpId, float>` carried per edge, exactly parallel to `energyByEdge`.
- Split: `q / nOut` per output. Merge: element-wise sum.
- A consumer op declares `{ consumes: OpId(s), produces: OpId }`; at its edge it removes up
  to the available input quantity and adds the product (scaled by its own combo multiplier).
- Unconsumed primitives simply arrive at the output as-is.

Exact consume math (partial consumption? ratios?) is defined per interactive op in
`../../effect-vocab/ops/interactives/` when those ops are authored (item 4).

---

## 4. Back-port TODO

- `../../compilation-system.md` §3 ("only a scalar energy flows") — add the payload channel.
- `../../compilation-system.md` §2/§5 + `../../legend.md` — state the leaf-node output rule.
- Playground — currently permits sinks on any open edge; update to the leaf rule.
