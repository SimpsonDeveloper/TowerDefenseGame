# Op: Mark / Sigil

**Kind:** primitive — applies a flat flag
**Applies:** Mark / Sigil · **State merge:** OR (present wins)

---

## Definition

Amethyst stamps a **Mark** flag (timed) on one enemy. It does nothing on its own — a tag
other ops read. Standalone.

- **Flat flag** (persistent gate): on/off + timer, no stacking.
- **State merge:** OR — a second Mark on an already-Marked enemy just keeps it Marked,
  refreshing the timer only if the new window is longer.

## Numbers

| Knob | Value | Why |
|---|---|---|
| Seconds per energy | 1 / 30 | Am–Ru off the 150-core starter turret pays Amethyst's toll of 20, so 130 crosses — a ~4.3s window |
| Max seconds | 8 | A ceiling, so a big lattice buys a window and not a permanent property of the enemy |

Duration is the **only** dimension the op has. The flag carries no magnitude — a mark is either on
or off — so energy can buy nothing but time, and the whole tuning surface is those two numbers.

Placeholders, chosen to make the shape legible rather than balanced: the window should outlive a
second tower's reload and expire well inside a wave.

## Re-marking

A re-mark **can only extend**. Timed flats merge by max (`../../vocab-overview/merge.md`), so a
cheap mark landing on a long one is ignored rather than replacing it, and two towers marking
together do not stack a window neither of them paid for. Nothing op-specific implements this — it
falls out of writing the state the ordinary way.

## What it is worth

Nothing, yet. **Focus** and **Detonate** are the consumers and neither exists, so a marked enemy
today is a line in the debug readout. That is the design rather than a gap: the op is the window
and the consumer is the payoff, and a state nobody reads is as inert as an op nobody implements.

No ticking face and no clock of its own — a timed flag is counted down by the enemy's own tick and
clears itself when it runs out.
