# Op: Focus

**Kind:** passive gate (persistent, not a spend)
**Reads:** Mark / Sigil · does **not** consume it

---

## Definition

While an enemy carries **Mark**, the tower's targeting **prioritizes it** — the tower
fires at the Marked enemy first, and overflow / secondary hits **bend onto** it. It does
not consume the Mark; the retarget lasts as long as the flag lives (painted-target).
Changes where shots go, not how much they hit for.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Mark** → no priority target; Focus does nothing.
- Remove **Focus** → the Mark just sits there; targeting is unchanged.
- Together: the Mark redirects fire only because Focus reads it, and Focus only bends fire
  because the Mark names a target ⇒ interactive.

## Open knobs
- How strongly overflow bends (fraction of secondary hits) · priority rule vs normal
  targeting · range within which the bend applies.
