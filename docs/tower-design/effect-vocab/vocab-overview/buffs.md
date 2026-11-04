# Enemy buffs

Part of the Effect Vocabulary overview — see `overview.md` for the index.

States written **by** crystals are the vocabulary. This file covers the other direction: what the
**enemy** brings. Purify (`../ops/primitives/purify.md`) is the only op that answers it.

---

## The roster: one buff

| Buff | State | Source | Effect |
|---|---|---|---|
| **Huddle regen** | `Regenerating` | standing near other enemies | heals a fraction of max HP per second, per neighbour |

That is the whole list, deliberately. The earlier plan was a set of four stat-boosters — +% move
speed, +% HP, +% attack, +% Mind — each granted by something. All four were dropped: a stat-boost
buff is just a tougher enemy with extra bookkeeping, and nothing in the vocabulary becomes more
interesting for having to read it.

## Natural, not granted

Huddle regen has **no source to kill**. Nothing casts it, no aura enemy carries it, no timer runs
out. It is true for exactly as long as the pack holds together, and false the moment it does not.

That is what makes it worth having:

- **It is the enemy's answer to AoE.** A splash build that spreads damage across a tight group is
  feeding the thing that undoes it.
- **It makes spacing matter.** The counterplay is not a dispel, it is breaking up the pack: a
  longer maze, a split path, a freeze on the leaders so the pack strings out. That hands roadmap
  item 5 (`../../impl-planning/combat/enemy-mind.md`) something to do — paths and deviation stop
  being flavour once the shape of the stream decides how hard it is to kill.
- **It is legible without a readout.** A clump that will not die looks like a clump that will not
  die.

## Why it is a state at all

`Regenerating` is **mirrored** from the proximity count every frame, never written by an op — the
same mechanism as `Shield-down` (`shield.md`), and for the same reason: the condition changes on
its own and anything stored would need every path that changes it to remember to update it.

It is in the state table so that ops can *read* it. Without that, Purify has nothing to aim at,
because there is no stored buff to delete.

## What Purify does about it

**Suppresses, not strips.** Deleting a derived state is meaningless — it is recomputed next frame
and the pack heals again. So Purify writes a timed shutdown that holds the regen off while
neighbours are still near, which is the same shape as Scramble against a shield: switch it off
without having to spend it. 6s off a starter turret, capped at 12.

**The shutdown is not a state.** A state exists so ops can read it, and no op asks how long is
left on another op's timer — that is bookkeeping, and it lives on the component that owns the
clock. `Regenerating` is in the table because it is a condition of the *enemy*, which the
vocabulary could plausibly consume one day; "Purify landed 4 seconds ago" never is.

So a suppressed enemy simply stops being `Regenerating`, and the debug readout prints the seconds
left straight off the component — lower-cased, with the damage-over-time total, to mark both as
bookkeeping rather than vocabulary.

See `../ops/primitives/purify.md`.
