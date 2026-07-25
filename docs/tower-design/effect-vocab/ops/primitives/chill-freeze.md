# Op: Chill → Freeze

**Kind:** primitive — applies a state (ladder slow → stop)
**Applies:** Chill stacks → Freeze · **State merge:** sum stacks (cap)

---

## Definition

Sapphire applies **Chill stacks**, each adding slow. Standalone.

- **Ladder** (slow → stop): stacks accumulate slow; past the freeze threshold `Tfreeze`
  the enemy **Freezes** — stops moving and skips deviation rolls.
- **State merge:** two Chill sources on one enemy sum stacks (capped). Stacks continue
  past `Tfreeze` (overstack is still tracked).

## Open knobs
- Slow per stack · `Tfreeze` threshold · freeze duration · stack cap.
