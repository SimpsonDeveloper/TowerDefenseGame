# Op: Short-circuit

**Kind:** opt-in, conditional spend
**Consumes:** Shield-down · **Burst type:** kinetic (HP)

---

## Definition

Short-circuit **consumes the Shield-down state** → an execute burst on the exposed enemy.
Inert unless the enemy is Shield-down. It is the **only consumer of Shield-down** (which
Scramble and raw damage-break produce), turning a defense-strip into a payoff instead of
just letting HP damage leak through.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Shield-down** → nothing to spend; Short-circuit is inert.
- Remove **Short-circuit** → Shield-down just lets normal HP damage through; no burst.
- Together: Short-circuit exists only to cash a dropped shield, and the dropped shield
  becomes a burst only because Short-circuit reads it ⇒ interactive.

## Open knobs
- Burst magnitude · whether it scales with the enemy's shield size · any splash.
