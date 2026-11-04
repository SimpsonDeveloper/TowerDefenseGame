# Op: Purify

**Kind:** primitive — utility (cleanse)
**Affects:** enemy buffs · **Applies:** nothing — a shutdown, not a state · **State merge:** max (the longest shutdown wins)

---

## Definition

Quartz (the pure catalyst) **suppresses an enemy's buffs** — currently the one buff there is,
huddle regen (`../../vocab-overview/buffs.md`). Standalone; a no-op on an enemy that is not
benefiting from one, which is any enemy travelling alone.

## Suppress, not strip

The original framing was "strip enemy buffs", which does not work, and the reason is worth
keeping: **huddle regen is derived, not stored.** It is recomputed from the enemy's neighbours
every frame, so there is no stored buff to delete — remove it and it is back before the next
frame draws. A `Clear` here would be a visibly useless op.

So Purify writes a **timed shutdown** instead: regen stays off for N seconds even while the pack
is still tight. That is the same shape as Scramble against a shield
(`scramble.md`) — switch the thing off without having to spend it — and it is the shape any
future buff will want too, since a buff worth having is a buff with a live source.

Duration is the only dimension, as with Mark: the shutdown has no magnitude.

## Numbers

| Knob | Value | Why |
|---|---|---|
| Seconds per energy | 1 / 24 | Qz–Qz off the 150-core starter turret pays Quartz's toll of 6 — the cheapest crystal there is — so 144 crosses, a 6s window |
| Max seconds | 12 | Generous, because the window must outlast the time to actually kill the pack. A shutdown that expires first accomplished nothing |

Duration is the only dimension; the shutdown has no magnitude. Quartz keeping nearly all the
energy that reaches it is the catalyst earning its keep: pure utility, no damage, a long window
for the toll of a rounding error.

A second Purify **extends and never shortens** — max, like every timed flat (`../../vocab-overview/merge.md`).

## Never wasted, unlike Scramble

A shield is a property of the unit, so scrambling an unshielded enemy does nothing — that is the
bet Scramble makes on the wave. Any enemy can end up in a crowd, so a shutdown landing on a lone
one is a **pre-emptive cleanse** rather than a miss: it still holds when the pack re-forms inside
the window.

The neighbour count keeps running while suppressed, so the moment the shutdown lapses the buff is
correct without waiting for a recount — and the readout still shows the pack.

## Open knobs
- Whether it should suppress *all* buffs or one, which stays academic while the roster is one.
