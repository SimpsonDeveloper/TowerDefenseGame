# Op: Frostburn

**Kind:** opt-in converter — conditional consumer of Burn, producer of chill stacks
**Needs:** Burn present on the target · **Feeds:** Chill → Freeze → Shatter

---

## Definition

Frostburn converts the target's remaining **Burn into chill stacks** — each pending fire
tick becomes `r` chill stack(s) (conversion ratio `r` = sub-knob). The Burn is spent
(removed); the added chill stacks push the enemy toward / past the Freeze threshold.
Conditional: needs Burn present, inert otherwise. Does not create Burn.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Burn** → nothing to convert; Frostburn is inert.
- Remove the **Freeze/Shatter** path → the converted chill just freezes with no payoff.
- Together: Frostburn only matters as the *join* — it changes what Burn *becomes*, not
  its raw numbers ⇒ interactive.

## Open knobs
- Conversion ratio `r` (fire tick → chill stacks).
