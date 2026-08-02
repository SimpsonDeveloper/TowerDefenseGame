# Crystal Tower — Compilation System

How a player-built lattice of crystals becomes a working tower weapon, and how that
weapon interacts with enemy states. This is the architecture overview; the concrete
vocabulary of effects lives in `effect-vocab/vocab-overview/overview.md` and the symbols in `legend.md`.

Status: design-stage synthesis. Effect-vocabulary topics and open questions live in
their own docs (see §7). Nothing here is implemented yet.

---

## 1. The core idea: form = function

The player assembles colored crystals into equilateral triangles on a triangular
lattice. That shape **compiles** into the tower's weapon. The arrangement *is* the
program — the same crystals in a different layout produce a different weapon.

Two things compile out of a lattice:
- a **weapon** (the energy it delivers), and
- an **ordered list of ops** (what it writes on / does to enemies — produced by crystal combos,
  ordered by lattice position, resolved on the enemy at hit time).

Delivery *shape* (nova, beam, arc, …) is **not** compiled here — it belongs to the tower
type (`effect-vocab/vocab-overview/delivery.md`), a separate axis.

---

## 2. Lattice & flow

- **Cell** = one triangle, holds at most one crystal. **▲ up = split** (1 in → 2 out,
  divides each stat). **▽ down = merge** (2 in → 1 out, sums each stat).
- The lattice is **bipartite**: ▲ only ever shares an edge with ▽. No two
  same-orientation cells are adjacent.
- Flow runs strictly **upward** ⇒ the build is a **DAG** (no cycles).
- **Sources** (S#) and **sinks** (T#) are **crystal-level terminals**, not edge sites, and
  **automatic** — the user never sets them. A crystal is a **source iff it is a leaf on its
  input side**, a **sink iff it is a leaf on its output side** (a lone crystal is both). A
  source crystal is seeded core energy directly; a sink crystal drains its post-toll energy to
  the weapon. The weapon = sum of all sinks. Because terminals are derived at leaf sides, the
  **leaf-input / leaf-output rule** holds by construction: a crystal's inputs are all-crystal
  **or** one source, its outputs are all-crystal **or** one sink. Details in
  `impl-planning/upgrades/op-flow.md` §3.

### Conservation routing

Routing **conserves every stat**: ▲ divides each stat among its outputs, ▽ sums its
inputs. Splitting trades magnitude for breadth.

**Energy** adds a **local toll**: `E_core` is split **equally** among the sources (no
weights), each crystal draws its own cost as the stream passes, ▲/▽ route the remainder.
Combo-op multiplier = energy reaching the crystal; below 0 = **debt** (inert until a ▽ merge
recovers it). Method + example in `energy-conservation.md`.

### Active edges

An internal edge is **active** only if it lies on a complete **source → sink path**:
**fed** (some live source reaches it from below) ∧ **productive** (some sink is
reachable from it above). Kill the sources below an edge and it goes inactive; cap off
the sinks above it and likewise. This is pure connectivity — no magnitudes — so we
**trim inactive edges first**, then the value pass routes energy through only what
survives. Effects fire only on active edges; *nothing happens on inactive routes.*

---

## 3. The stream (what flows)

Two things ride the routing, both split by ▲ and summed by ▽:

- **Energy** — one scalar magnitude, the shared multiplier every op scales by. It carries
  the **local toll**: sources split `E_core` **equally** among themselves (no weights); each
  crystal draws its own cost as the stream passes; a ▲ divides the remainder, a ▽ sums
  (`energy-conservation.md`).
- **Ops** — the shot's **ordered list** of `(op, quantity)`. Each active combo produces its
  op once, at quantity = the energy reaching the downstream crystal. The list does **not**
  flow, split, or merge like energy, and **nothing is consumed at compile time**: a primitive
  and the interactive that will later eat it both appear in the shot. Order is fixed by lattice
  geometry — **vertical first** (lowest gem first, so a higher gem is always last), **horizontal
  second** (leftmost first), anchored to the producing (downstream) gem. Full model in
  `impl-planning/upgrades/op-flow.md`.

Everything else an op does — which enemy bar it hits (HP vs R — see §7), what state it
writes, **what it consumes**, how its quantity maps to effect — is **op behavior**, resolved
on the enemy at **hit time** by walking the ordered list one op at a time
(`effect-vocab/vocab-overview/states.md` → *Shot resolution*), and authored per-op under
`effect-vocab/ops/`. The compiler here only routes energy, names each combo's op, and orders
them.

---

## 4. Crystals as operators

A crystal does nothing on its own. Its jobs are:
- **route energy** — orientation on the lattice sets arity: ▲ splits (divides), ▽ merges
  (sums), and it draws its cost as the stream passes.
- **form combos** — two crystals **adjacent in the flow** produce the op named in the
  combo matrix, scaled by the energy arriving at the downstream crystal.

Each crystal also has an **element** (flavor: Fire/Ice/Lightning/Nature/Arcane-Mind/Pure)
that inspired its ops. The roster + combo matrix live in `playground/crystal-core.js`; the source of
truth for pairs → ops is `effect-vocab/vocab-overview/combo-matrix.md`.

Quartz is the identity/**catalyst** — pure routing wire whose combos spend a neighbor's
ladder (e.g. Quartz + Sapphire = Shatter, consuming Freeze).

---

## 5. Compilation pipeline

Conceptual passes (order matters; structural first, then values, then effects):

1. **Terminals** (auto): source = leaf-input crystal, sink = leaf-output crystal (a lone
   crystal is both). Derived from geometry, always on, never user-set.
2. **Productivity pass** (top → bottom): mark nodes/edges that can reach a sink.
3. **Fed pass** (bottom → top): mark nodes reachable from a source. Active = both.
4. **Energy routing** (bottom → top): seed each source with `E_core / nSources` (equal, no
   weights); each crystal draws its cost (local toll); ▲ divides the remainder, ▽ sums inputs;
   accumulate at sinks.
5. **Op production**: on **active edges**, each adjacent crystal pair produces the op named
   in the combo matrix (`effect-vocab/vocab-overview/combo-matrix.md`), scaled by the energy
   arriving at the downstream crystal.
6. **Weapon energy = sum of sinks.** Output: the delivered energy + the ordered op list.

Energy: each source is seeded `E_core / nSources` (equal split); every crystal draws its cost
locally as the stream passes (`energy-conservation.md`). Net after all tolls = `E_core − Σ cost`.
Energy dead-ending in a sinkless branch is `lostEnergy` (wasted).

---

## 6. Delivery — out of scope here

Delivery shape (nova, splash, arc, field, beam, …) is **not** compiled from the lattice.
It is owned by the **tower type** — a separate axis — and detailed in
`effect-vocab/vocab-overview/delivery.md`. The crystals supply *what the hit does* (energy + ops);
the tower type supplies *how it is delivered*. The two are orthogonal.

---

## 7. Beyond compilation — where the rest lives

This doc ends at the compiled output: **routed energy + a payload of named ops per shot**.
What those ops *do* to enemies — states, consumers, bars, bounding, authoring — is the
**effect vocabulary**, not the compiler. Canonical homes:

| Topic | Doc |
|---|---|
| Triads (producer → state → consumer), the two op classes, interactive test | `effect-vocab/vocab-overview/overview.md` |
| States table · ladders vs flat · many-to-many · **state coupling** (runtime wiring ⟂ to the lattice) | `effect-vocab/vocab-overview/states.md` |
| The **two merges** — lattice ▽ (compile-time) vs state merge (runtime) | `effect-vocab/vocab-overview/merge.md` |
| Enemy bars (HP / R) · which bar an op hits | `effect-vocab/vocab-overview/damage.md` |
| R (illusion resistance) mechanic · deviation `f(R)` | `effect-vocab/vocab-overview/illusion.md` |
| Bounding — compile-time impact cap (product of fan-outs, over-budget = illegal) | `impl-planning/upgrades/compiler-core.md` |
| Bounding — runtime recursion (delivery-layer concern) | `effect-vocab/vocab-overview/delivery.md` |
| Authoring philosophy · investment axes (A / space / B) | `effect-vocab/vocab-overview/overview.md` · `legend.md` |
| Open questions & tuning knobs | `effect-vocab/vocab-overview/open-questions.md` |

The one insight worth keeping here: **lattice coupling** (this doc — combos on adjacent
active edges, compile-time) and **state coupling** (runtime, an op writes a flag another op
reads — possibly a *different tower*) are two perpendicular wiring systems. Compilation owns
only the first.
