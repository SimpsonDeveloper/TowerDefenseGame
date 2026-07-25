# Op: Scramble

**Kind:** primitive — applies a flat flag
**Applies:** Shield-down · **State merge:** OR (present wins)

---

## Definition

Citrine's EMP **disables an enemy's Shield** directly (no damage needed), producing the
**Shield-down** state. Standalone.

- While Shield-down, HP damage bypasses the shield and lands directly. Shield does not
  protect R either way.
- On an enemy with **no shield** (HP-only) there is nothing to disable — the effect is
  wasted (see the shield : HP spectrum in `../../vocab-overview/shield.md`).
- **State merge:** OR — already-down stays down (optionally refresh the timer).

## Open knobs
- Shield-down duration · whether Scramble also deals any damage.
