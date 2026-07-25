# Op: Flareup

**Kind:** opt-in, conditional spend (charge-and-spend consumer)
**Consumes:** Burn · **Burst type:** kinetic (HP)

---

## Definition

Flareup **consumes the enemy's remaining Burn stacks** and converts the burn's over-time
future into immediate damage: `F = Cflare × N`, where `N` = Burn stacks at the instant it
fires. Inert unless the target carries Burn. Spends all Burn (charge resets after firing).
Trades a slow DoT tail for a burst now — tempo.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Burn** → nothing to detonate; Flareup is inert.
- Remove **Flareup** → Burn just ticks over its full duration; no burst.
- Together: Flareup changes what Burn *is* (future → now), and Burn is the charge Flareup
  *spends* ⇒ interactive.

## Open knobs
- `Cflare` (burst per burn stack) · whether it spends all stacks or a fraction.
