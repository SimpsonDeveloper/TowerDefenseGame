# Op: Shatter

**Kind:** opt-in, conditional spend (charge-and-spend consumer)
**Consumes:** Freeze **or** Brittle · **Burst type:** kinetic (HP)

---

## Definition

Shatter bursts **linear in the charge it spends**: `S = Cshatter × N`. Inert unless the
target is Frozen or Brittle. Spends the Freeze/Brittle (charge resets after firing).

- **Freeze path:** `N` = the enemy's chill stacks at the instant it fires; must be Frozen
  (`stacks ≥ Tfreeze`). Overstacked chill keeps raising `N` — more chill = bigger burst.
- **Brittle path:** Brittle is flat (no stacks) → fixed charge `Nbrittle`.
- Linear by default; optional cap = sub-knob.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Freeze/Brittle** → Shatter has no charge to spend; it's inert.
- Remove **Shatter** → the freeze/brittle just stalls or chips the enemy; the stacked
  chill never converts into a burst.
- Together: freeze changes what Shatter *does* (burst scales with stacks), Shatter
  changes what freeze is *worth* (a stall becomes a payoff) ⇒ interactive.

## Open knobs
- `Cshatter` (per-stack burst) · `Nbrittle` (Brittle's flat charge) · `Tfreeze` · optional cap.
