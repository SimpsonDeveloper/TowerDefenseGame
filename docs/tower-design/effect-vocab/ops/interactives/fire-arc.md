# Op: Fire Arc

**Kind:** reactive chain (conditional on Burn)
**Reacts to:** Burn · **Bolt:** red

---

## Definition

A **red arc** that leaps from a burning enemy to nearby **burning** enemies, dealing
damage along the chain. Burn is the conductor — the arc only jumps between enemies that
carry it, and reach grows with how much of the wave is on fire. Inert on a wave with no
Burn painted.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Burn** → nothing conducts; the arc is inert.
- Remove **Fire Arc** → Burn just DoTs each enemy alone; no chain.
- Together: Burn becomes a conductive network only because the arc reads it ⇒ interactive.

## Open knobs
- Arc damage · jump count / reach · falloff per jump · does the arc scale with Burn stacks.
