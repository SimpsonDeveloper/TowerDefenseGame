# Op: Acid Arc

**Kind:** reactive chain (conditional on Corrode)
**Reacts to:** Corrode · **Bolt:** green

---

## Definition

A **green arc** that leaps from a corroded enemy to nearby **corroded** enemies, dealing
damage along the chain. Corrode is the conductor — the arc only jumps between enemies
that carry it, and reach grows with how much of the wave is corroded. Inert on a wave
with no Corrode painted.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Corrode** → nothing conducts; the arc is inert.
- Remove **Acid Arc** → Corrode just DoTs each enemy alone; no chain.
- Together: Corrode becomes a conductive network only because the arc reads it ⇒ interactive.

## Open knobs
- Arc damage · jump count / reach · falloff per jump · does the arc scale with Corrode stacks.
