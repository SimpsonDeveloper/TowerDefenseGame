# Op Flow — the compiled shot's ordered op list

What the compiler emits alongside energy: an **ordered list of ops**. **Roadmap item 2** —
layered on the compiler core (**item 1**, `compiler-core.md`), which stubs it. This is a
**compilation-system** concern: the ordered list is part of what the compiler *emits*.

**Consumers are not resolved here.** Nothing in this pass makes an op *do* anything, and
nothing is consumed at compile time. Producing an ordered list is all the compiler does; the
enemy walks that list at **hit time** and resolves consumption then
(`../../effect-vocab/vocab-overview/states.md` → *Shot resolution*). Already validated in the
playground — port that.

Design goal: **keep it simple.** Ops are named and ordered by the lattice; magnitude is the
combo's energy. No conversion constants, no in-lattice state.

Two rules first stated here — flagged for back-port to `../../compilation-system.md` and
`../../legend.md` (see §6).

---

## 1. Scope (deliberately thin)

This item does **not** implement any op and does **not** consume anything. It computes, for
one shot, the **ordered sequence of ops with their energy multipliers**. Concretely:

- **Op names + quantity only.** An op is an `OpId` + a float quantity (= energy arriving at
  its downstream gem, floored at 0). No behavior, no damage, no states written on an enemy.
- **Producers come from the effect vocabulary**, not new tables here: which combo produces
  which op → `../../effect-vocab/vocab-overview/combo-matrix.md`.
- **No consumers in the compiler.** Which interactive op consumes which primitive (the op
  file's **`Consumes:`** header) is a **hit-time** concern, not a compile-time one. The old
  "interactive op eats the upstream primitive during routing" model is **gone** — a primitive
  and the interactive that will consume it both appear in the shot; the enemy resolves it.
- **Order is the deliverable.** The list is sorted by lattice geometry (§3) so the enemy can
  apply it deterministically.

Done when: a lattice compiles to an **ordered** `(OpId, quantity)` list matching the
hand-worked example below.

---

## 2. The model

Energy is not the only thing the compiler emits. Alongside the routed energy it emits a
**shot** — an ordered list of `(OpId, quantity)`.

1. **Produce.** Each **active** combo edge produces its op once, with
   **quantity = energy arriving at the downstream crystal** (floored at 0, per
   `../../energy-conservation.md`). Debt / zero-energy edges produce nothing.
2. **Collect.** Every produced op across the whole lattice goes into one flat list for the
   shot. There is no bag, no split/merge of op quantities, and no consumption — a merge does
   not sum op quantities and a split does not divide them. (Energy still splits/merges; the
   op list does not.) Each op simply carries the energy at the gem that produced it.
3. **Order.** Sort the list by lattice geometry (§3).
4. **Emit.** The ordered list *is* the shot's payload. What each op does — and what consumes
   what — happens later, on the enemy (`../combat/primitives.md`,
   `../../effect-vocab/vocab-overview/states.md`).

### Worked example

Chain (flow upward): Ruby → Ruby → Sapphire. Adjacent pairs produce **Burn** (Ruby·Ruby, at
the middle gem) and **Frostburn** (Ruby·Sapphire, at the top gem). The middle gem is lower
than the top gem, so the shot is the ordered list:

```
1. Burn      × (energy at middle gem)
2. Frostburn × (energy at top gem)
```

Both ride the shot. **No consumption in the compiler.** When this shot hits an enemy, the
enemy applies `Burn` (adds Burn stacks), then applies `Frostburn`, which — *now, at hit time,
reading the enemy's current Burn* — converts that Burn into chill
(`../../effect-vocab/ops/interactives/frostburn.md`).

---

## 3. Ordering rule (leaf-node evaluation order)

The order the enemy applies ops in is fixed by **where each op is produced on the lattice**.
Each op is anchored to its **downstream (producing) gem** — the crystal the combo's energy
arrives at, whose energy is the op's quantity.

- **Vertical is first order.** Lower gems come first; a **higher gem is always evaluated
  last**, regardless of its horizontal position. (This matches flow order: ops produced
  deeper in the stream are applied before ops produced nearer the weapon exit.)
- **Horizontal is second order.** Among gems at the same height, **leftmost first**.
- **Tiebreak.** Two combos landing on the **same** downstream gem (a ▽ merge has two inputs)
  order by the **leftmost upstream gem** first, then by op name for determinism.

In lattice coordinates this is: sort by row (lower row first), then column (left first), then
upstream column, then name. The playground uses gem-centroid `(cy desc, cx asc)` with the same
tiebreak; the C# port should sort on lattice `(row, col)`, not pixels.

---

## 4. Terminal rules (leaf-input / leaf-output) — automatic, no weights

**Sources and sinks are crystal-level *terminals*, not edge sites, and AUTOMATIC.** The user
never sets them (a requirement for the C# impl too — `compiler-core.md`). The compiler derives
them from geometry on every compile:

- A crystal is a **source iff it is a leaf on its input side** (no crystal feeds it) — seeded
  by the core.
- A crystal is a **sink iff it is a leaf on its output side** (no crystal sits above it) —
  drains to the weapon.
- A **lone** crystal is both — the minimal tower.

Because terminals are *derived* at leaf sides, the two symmetric constraints hold **by
construction** (they cannot be violated):

- **Leaf-output.** A crystal's outputs are **all-crystal** or **one sink** — never mixed.
- **Leaf-input.** A crystal's inputs are **all-crystal** or **one source** — never mixed,
  never two.

**Energy: equal split, no weights** (also a C# requirement). The core energy is divided
**equally** among the sources: each of `n` sources is seeded `E_core / n` directly (input arity
ignored). A sink crystal delivers its post-toll energy to the weapon. A terminal binds to the
whole crystal, not an edge.

- Rationale: a terminal is a *terminus/origin*. Deriving them at leaves keeps each stream's
  fate singular at both ends and the energy accounting unambiguous; an equal split keeps the
  model simple (no per-source tuning to reason about).
- Enforcement: **none needed** — auto-derivation makes the leaf rules structural. The UI just
  *displays* the derived terminals (`lattice-ui.md` §3). The playground already does this.

---

## 5. Data shape (proposed, simple)

- `Shot = List<(OpId op, float qty)>` — **ordered**; the sort of §3 is applied once at compile.
- No `Dictionary`, no per-edge payload map, no consume step. Each entry = one active combo.
- Consumption math (partial? ratios? multiplier scaling?) is authored later **per interactive
  op** and applied by the enemy at hit time (item 4, `../combat/primitives.md`,
  `../../effect-vocab/ops/interactives/`). The 1-to-1 driver lives there, **not** in the
  compiler.

---

## 6. Back-port TODO

- ✅ `../../compilation-system.md` §3 — payload is an **ordered op list**; consumption is
  deferred to the enemy at hit time (no in-lattice consume).
- ✅ `../../compilation-system.md` §2 + `../../legend.md` — terminal rules (leaf-input /
  leaf-output) + crystal-level source/sink; ordered-op-list + op-order terms added.
- ✅ `../../effect-vocab/vocab-overview/states.md` — **Shot resolution** section: the enemy
  walks the ordered list at hit time and resolves consumers (Frostburn example).
- ✅ Playground — the shot is an ordered list; no compile-time consumption.
