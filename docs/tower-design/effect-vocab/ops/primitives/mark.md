# Op: Mark / Sigil

**Kind:** primitive — applies a flat flag
**Applies:** Mark / Sigil · **State merge:** OR (present wins)

---

## Definition

Amethyst stamps a **Mark** flag (timed) on one enemy. It does nothing on its own — a tag
other ops read. Standalone.

- **Flat flag** (persistent gate): on/off + timer, no stacking.
- **State merge:** OR — a second Mark on an already-Marked enemy just keeps it Marked
  (optionally refresh the timer).

## Open knobs
- Mark duration · whether a re-mark refreshes the timer.
