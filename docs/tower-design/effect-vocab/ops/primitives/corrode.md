# Op: Corrode

**Kind:** primitive — applies a state (ramping DoT)
**Applies:** Corrode · **State merge:** sum stacks (cap)

---

## Definition

Emerald's rate applies **Corrode stacks** — acid that eats HP over time. Standalone.

- **Ramps:** damage is nonlinear in stack count (more stacks = disproportionately more
  decay).
- **State merge:** two Corrode sources on one enemy sum stacks (capped).

## Open knobs
- Tick per stack · ramp curve (how nonlinear) · stack cap.
