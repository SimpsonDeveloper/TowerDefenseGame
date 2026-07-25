# Op: Burn

**Kind:** primitive — applies a state (ladder DoT)
**Applies:** Burn · **State merge:** sum stacks (cap)

---

## Definition

Ruby's ignition applies **Burn stacks** that tick HP damage over time. Standalone.

- **Ladder** (smolder → blaze): low stacks smolder (small ticks); past a threshold they
  blaze (bigger / faster ticks).
- **State merge:** two Burn sources on one enemy sum stacks (capped).

## Open knobs
- Tick damage per stack · smolder→blaze threshold · duration · stack cap.
