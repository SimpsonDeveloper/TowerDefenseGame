# Op: Accelerant

**Kind:** passive multiplier (no direct damage)
**Reacts to:** any DoT on the enemy

---

## Definition

Accelerant deals **no damage itself**. While applied, every **DoT already on the enemy**
(Burn, Corrode, …) **ticks faster** — a rate multiplier on someone else's work. A support
op that silently boosts whatever DoT build you already run; worthless on an enemy with no
DoT.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove the **DoT** → nothing to speed up; Accelerant is inert.
- Remove **Accelerant** → the DoT ticks at its base rate.
- Together: Accelerant only matters as a multiplier on a DoT, and the DoT only spikes
  because Accelerant sped it ⇒ interactive.

## Open knobs
- Rate multiplier (× tick speed) · whether it also affects DoT duration · stacking with
  a second Accelerant.
