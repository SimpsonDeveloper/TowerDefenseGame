# Op: Frostburn

**Kind:** opt-in converter — conditional consumer of Burn, producer of Frostburn stacks
**Needs:** Burn present on the target · **Feeds:** Chill → Freeze → Shatter

---

## Definition

Frostburn does an **initial consume of Burn**, converting it **1-to-1** into **Frostburn
stacks** (conversion ratio `r`, default 1 = sub-knob). The Burn is spent (removed).

Frostburn is a retained stack state that **ticks over time**: on each tick it applies **chill**
(where Burn would tick fire damage, Frostburn ticks chill instead). The accumulating chill
pushes the enemy toward / past the Freeze threshold. Conditional: needs Burn present, inert
otherwise. Does not create Burn.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Burn** → nothing to convert; Frostburn is inert.
- Remove the **Freeze/Shatter** path → the converted chill just freezes with no payoff.
- Together: Frostburn only matters as the *join* — it changes what Burn *becomes*, not
  its raw numbers ⇒ interactive.

## Open knobs
- Conversion ratio `r` (fire tick → chill stacks).
