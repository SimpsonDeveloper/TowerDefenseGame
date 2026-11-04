# Op: Purify

**Kind:** primitive — utility (cleanse)
**Affects:** enemy buffs · **State merge:** max (the longest shutdown wins)

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

## Open knobs
- Seconds of shutdown per unit of edge energy, and the cap.
- Whether a second Purify extends (max, like every other timed flat) — almost certainly yes, for
  consistency.
- Whether it should suppress *all* buffs or one, which stays academic while the roster is one.
