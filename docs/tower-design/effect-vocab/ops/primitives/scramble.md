# Op: Scramble

**Kind:** primitive — disables a defensive layer
**Applies:** Shield-down · **State merge:** OR (present wins)

---

## Definition

Citrine's EMP **disables an enemy's Shield** directly, producing the **Shield-down** state.
Standalone, and it deals no damage.

- **It does not spend the shield.** Breaking a shield with damage means paying its worth in HP
  first; Scramble skips that bill entirely and leaves the points untouched.
- While Shield-down, HP damage lands unblocked. Shield does not protect Mind either way.
- On an enemy with **no shield** there is nothing to disable and the crystal is wasted (see the
  shield : HP spectrum in `../../vocab-overview/shield.md`). Nothing refunds it — that waste is the
  bet.
- **State merge:** the longest shutdown wins, as for any timed flat.

## Numbers

Ported to `scripts/combat/core/ops/Scramble.cs` as `ScrambleTuning`.

| Knob | Default | What it does |
|---|---|---|
| `SecondsPerEnergy` | `1/40` | Edge energy → seconds of shutdown. |
| `MaxSeconds` | `6.0` | Ceiling on one application. |

Against the shipped starter turret's 150 core, a Citrine·Citrine edge carries ~138 — about **3.45
seconds**, with the cap stopping a large lattice from switching a shield off for a whole wave.

Scramble deals **no damage at all**, which is deliberate: its entire value is tempo against a
shielded wave, and giving it a damage floor would blunt the bet.

## Where the state comes from

Scramble queues a shutdown out to the shield, the same way damage is queued; the `Shield-down`
state is **mirrored back** from the shield each frame rather than written by the op
(`../../../impl-planning/combat/primitives.md` §3). The shield goes down two ways — emptied by
damage, or disabled here — and comes back up on its own timer, so anything stored would need every
one of those paths to remember to update it.

## Status

**Built** — `scripts/combat/core/ops/Scramble.cs`, one `IOp` face. The shield itself is
`ShieldComponent`, beside `HealthComponent`, which routes damage through it.

Its consumer, Short-circuit, is not built: that is the only thing that turns a defence-strip into a
payoff rather than just letting HP damage through
(`../interactives/short-circuit.md`).
