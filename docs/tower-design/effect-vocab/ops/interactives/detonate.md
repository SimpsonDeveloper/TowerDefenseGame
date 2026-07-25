# Op: Detonate

**Kind:** opt-in, conditional spend
**Consumes:** Mark / Sigil · **Burst type:** kinetic (HP)

---

## Definition

Detonate **consumes the Mark** on a target → a fixed burst on that enemy (`Ddet`
damage). Inert unless the target is Marked. The flag is currency: gone after firing.
Burst is fixed (Mark is a flat flag).

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Mark** → nothing to detonate; Detonate is inert.
- Remove **Detonate** → the Mark just sits; no burst.
- Together: Detonate exists only to spend the Mark, and the Mark is what turns this
  combo from nothing into a targeted burst ⇒ interactive.

## Open knobs
- `Ddet` (burst magnitude) · whether burst has any splash on the marked enemy.