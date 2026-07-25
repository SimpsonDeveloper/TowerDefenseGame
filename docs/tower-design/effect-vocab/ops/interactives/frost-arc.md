# Op: Frost Arc

**Kind:** reactive chain (conditional on Chill)
**Reacts to:** Chill · **Bolt:** blue

---

## Definition

A **blue arc** that leaps from a chilled enemy to nearby **chilled** enemies, dealing
damage along the chain. Chill is the conductor — the arc only jumps between enemies that
carry it, and reach grows with how much of the wave is chilled. Inert on a wave with no
Chill painted.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Chill** → nothing conducts; the arc is inert.
- Remove **Frost Arc** → Chill just slows each enemy alone; no chain.
- Together: Chill becomes a conductive network only because the arc reads it ⇒ interactive.

## Open knobs
- Arc damage · jump count / reach · falloff per jump · does the arc scale with Chill stacks.
