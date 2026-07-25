# Op: Dissolve

**Kind:** opt-in, conditional spend (charge-and-spend consumer)
**Consumes:** Corrode · **Leaves:** Brittle · **Type:** kinetic (HP)

---

## Definition

Dissolve **consumes the enemy's Corrode stacks** and does two things:

1. **Execute chunk** — instant HP damage scaling with the corrode stacks spent
   (`D = Cdissolve × N`, `N` = corrode stacks). Bigger on high-corrode targets.
2. **Leaves Brittle** — applies the flat **Brittle** state.

Inert unless the target carries Corrode. Spends the Corrode (charge resets after firing).

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Remove **Corrode** → nothing to dissolve; Dissolve is inert.
- Remove **Dissolve** → Corrode just ticks as a DoT; it never converts to a burst and
  never leaves Brittle.
- Together: Dissolve changes what Corrode *becomes* (a spend + a Brittle hand-off),
  Corrode is the charge it spends ⇒ interactive.

## Open knobs
- `Cdissolve` (execute per corrode stack) · Brittle amount applied · spend-all vs partial.
