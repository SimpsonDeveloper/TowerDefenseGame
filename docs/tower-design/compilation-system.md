# Crystal Tower — Compilation System

How a player-built lattice of crystals becomes a working tower weapon, and how that
weapon interacts with enemy states. This is the architecture overview; the concrete
vocabulary of effects lives in `effect-vocab/vocab-overview/overview.md` and the symbols in `legend.md`.

Status: design-stage synthesis. Decided rules are marked; open questions are listed
at the end. Nothing here is implemented yet.

---

## 1. The core idea: form = function

The player assembles colored crystals into equilateral triangles on a triangular
lattice. That shape **compiles** into the tower's weapon. The arrangement *is* the
program — the same crystals in a different layout produce a different weapon.

Two things compile out of a lattice:
- a **weapon** (the energy it delivers), and
- a set of **ops** (what it writes on / does to enemies — produced by crystal combos).

Delivery *shape* (nova, beam, arc, …) is **not** compiled here — it belongs to the tower
type (`effect-vocab/vocab-overview/delivery.md`), a separate axis.

---

## 2. Lattice & flow

- **Cell** = one triangle, holds at most one crystal. **▲ up = split** (1 in → 2 out,
  divides each stat). **▽ down = merge** (2 in → 1 out, sums each stat).
- The lattice is **bipartite**: ▲ only ever shares an edge with ▽. No two
  same-orientation cells are adjacent.
- Flow runs strictly **upward** ⇒ the build is a **DAG** (no cycles).
- **Sources** (S#) seed energy; **sinks** (T#) collect it. The weapon = sum of all sinks.

### Conservation routing (decided)

Routing **conserves every stat**. ▲ divides each stat among outputs; ▽ sums inputs.
- Splitting to hit more targets / more bars **divides** power — breadth costs magnitude.
- Deep nesting dilutes per-hit power automatically, so recursion needs no arbitrary cap.

**Energy** (the crystals' shared multiplier) additionally follows a **local-toll**
rule: each source is seeded the full `E_core`, and each crystal **draws its own cost**
from the stream as it passes. The energy arriving at a crystal is the multiplier for
its combo-op; energy below 0 is **debt** (op inert until a ▽ merge overcomes it). Full
method + worked example in `energy-conservation.md`.

### Active edges (decided)

Effects only happen on **active** internal edges = **productive** (can reach a sink)
∧ **fed** (reachable from a source). Active-ness is structural — independent of
magnitudes — so there is no circular dependency with a value pass. *Nothing happens
on inactive routes.*

---

## 3. The stream (what flows)

The only thing that flows is a scalar **energy** magnitude. There are no per-stat
"facets" — energy is the shared multiplier every op scales by. Routing follows the
**local-toll** rule (`energy-conservation.md`): sources are seeded the full `E_core`;
each crystal draws its own cost as the stream passes; a ▲ divides the remainder, a ▽ sums.

Everything else an op does — which enemy bar it hits (HP vs R, see §9), what state it
writes, its magnitude curve — is **op behavior**, authored per-op under
`effect-vocab/ops/` and implemented in the port. The compiler here only routes energy and
names the ops each combo produces.

---

## 4. Crystals as operators

A crystal does nothing on its own. Its jobs are:
- **route energy** — orientation on the lattice sets arity: ▲ splits (divides), ▽ merges
  (sums), and it draws its cost as the stream passes.
- **form combos** — two crystals **adjacent in the flow** produce the op named in the
  combo matrix, scaled by the energy arriving at the downstream crystal.

Each crystal also has an **element** (flavor: Fire/Ice/Lightning/Nature/Arcane-Mind/Pure)
that inspired its ops. The roster + combo matrix live in `crystal-core.js`; the source of
truth for pairs → ops is `effect-vocab/vocab-overview/combo-matrix.md`.

Quartz is the identity/**catalyst** — pure routing wire whose combos spend a neighbor's
ladder (e.g. Quartz + Sapphire = Shatter, consuming Freeze).

---

## 5. Compilation pipeline

Conceptual passes (order matters; first two are structural, then values, then effects):

1. **Productivity pass** (top → bottom): mark nodes/edges that can reach a sink.
2. **Fed pass** (bottom → top): mark nodes reachable from a source. Active = both.
3. **Energy routing** (bottom → top): seed sources with `E_core` split by weight; each
   crystal draws its cost (local toll); ▲ divides the remainder, ▽ sums inputs; accumulate
   at sinks.
4. **Op production**: on **active edges**, each adjacent crystal pair produces the op named
   in the combo matrix (`effect-vocab/vocab-overview/combo-matrix.md`), scaled by the energy
   arriving at the downstream crystal.
5. **Weapon energy = sum of sinks.** Output: the delivered energy + the ops written.

Energy: each source is seeded `E_core`; every crystal draws its cost locally as the
stream passes (`energy-conservation.md`). Net after all tolls = `E_core − Σ cost`.
Energy dead-ending in a sinkless branch is `lostEnergy` (wasted).

---

## 6. Delivery — out of scope here

Delivery shape (nova, splash, arc, field, beam, …) is **not** compiled from the lattice.
It is owned by the **tower type** — a separate axis — and detailed in
`effect-vocab/vocab-overview/delivery.md`. The crystals supply *what the hit does* (energy
+ ops); the tower type supplies *how it is delivered*. The two are orthogonal.

---

## 7. Status effects: producer → state → consumer (triads)

The authored unit is the **triad**, not the lone status:

- **Producer** — an op (crystal or combo) that writes a detectable state on the enemy.
- **State** — a flag/meter the enemy carries (Burn, Chill→Freeze, Brittle, R, ...).
  May be a small FSM with thresholds (Chill stacks → Freeze) — the only "derivation"
  is a counter crossing a line. No physics.
- **Consumer** — an op that reads the state and does something **interactive**
  (non-additive).

**Interactive test** (decided gate): remove one part — does the other's behavior
change? No → additive (reject as a fake combo). Yes → interactive (keep).

Producers and consumers are **many-to-many** (keeps the authored set small):
Shatter consumes Freeze *and* Brittle; Nova consumes Clustered; Death consumes Hex.

### Ladders vs flat states

- **Ladder** = staged escalation (Chill→Freeze, smolder→blaze). Enables the
  **charge-and-spend** shape: a ladder producer paired with a consume-the-stack verb
  (Shatter/Flareup/Dissolve). This is the reusable engine of the system.
- **Flat** = on/off + timer (Mark, Scrambled, Hexed). These are *enablers* (gate
  other effects), not charge-and-spend.

---

## 8. Two coupling channels (where ops interact)

1. **Lattice coupling** — ops adjacent on the lattice, at compile time, in one tower.
   This is the build/edge interaction (the M[A][B] matrix, upgraded from a scalar to
   actual op production).
2. **State coupling** — an op writes a flag on the enemy; another op reads it at
   run-time. The reader can be a different lattice spot, a **different tower**, or the
   same tower later. State is the channel where ops that are *not* lattice-neighbors
   interact (e.g. Tower 1 freezes, Tower 2 shatters).

Writing a state is uniform; reading differs by how the reading op is built. State is
**not** a sealed layer — it is a second wiring system perpendicular to the lattice.

---

## 9. Enemy bars

- **HP bar** — drained by physical ops.
- **R bar (illusion resistance)** — a per-enemy "mind shield", set at spawn, drained
  permanently by mind ops (Mind-damage). Enemies roll for path **deviation** at intervals;
  per-roll chance is a function of current R. Frozen/inactive enemies skip rolls.
  R → 0 = enemy permanently railroaded. Detail + design properties in `effect-vocab/vocab-overview/illusion.md`.

Which bar an op hits is **op behavior** (defined per-op), but both bars scale with the same
**energy** reaching the op — so the same build invested in a mind combo drains R as hard as
a physical combo drains HP.

---

## 10. Bounding (anti-fork-bomb)

Composition is rich but must stay finite:

- **Compile-time nesting is already bounded** by the finite DAG (depth ≤ path length),
  and by energy: the local toll drives a deep chain into debt, so far branches go inert.
- **Compile-time impact cap**: per-shot impact count = product of fan-outs; compute it,
  show it, make over-budget builds illegal. (Energy dilution discourages deep nesting for
  magnitude but not for cheap condition-spam, so an explicit cap stays.)
- **Runtime recursion** (a repeating trigger that spawns a fresh attack which re-feeds the
  trigger) is a *delivery*-layer concern, bounded with the tower types
  (`effect-vocab/vocab-overview/delivery.md`), not here.

Industry precedent: PoE support gems / MTG state-based actions — bounded, stratified
composition with explicit anti-recursion. "Pure emergence" is fenced off, not shipped.

---

## 11. Authoring philosophy

You author a **finite, closed vocabulary**: statuses, their definitions, and the
interaction rules (producers/consumers/reactions). You do **not** author every weapon.
Emergence = the player's **reachable build-space** over that vocabulary — finite words +
grammar → effectively unbounded sentences.

Investment axes (see `legend.md`): **A** raise E_core (power ceiling) · **space**
buy lattice cells · **B** generators (planned).

---

## 12. Open questions (to firm up before build)

- **Force / gravity family** (Mire/Lure/Repel/Clustered): no crystal. Fork — add one
  earth/force crystal (7 total) · fold into arcane · cut the states.
- **Hybrid crystals (option 2)**: keep, but design after primitive ops are fleshed.
- **Frostburn** and other combos: give *interactive* definitions (must pass the test).
- **Deviation `f(R)`**: linear ramp vs threshold flip.
- **Strained element mappings**: emerald→acid, amethyst→arcane(mind) — accept or rework.
- **Node vs edge authoring** for complex effects (they are duals; running both is open).
- Reserved combo cells: fill only as new ops earn them.
