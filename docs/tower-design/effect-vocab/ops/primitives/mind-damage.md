# Op: Mind-damage (Illusion)

**Kind:** primitive — routes damage to a bar
**Affects:** R (illusion resistance) · not a stacking state

---

## Definition

An **Am–Am combo** (two amethyst crystals in sequence — this native op) tags a stream's
`type → mind`, so its **power routes to the enemy's R bar** instead of HP. A single
amethyst does not: converting kinetic → mind takes two in sequence. It drains **R** —
the "mind shield" — permanently; at R = 0 the enemy is railroaded and can never deviate.

- Not a stacking status — damage to a meter, like HP damage but on the R bar. No merge
  rule (R is an innate per-enemy meter, only drained).
- Magnitude comes from whatever **power** the stream carries.
- To hit both HP and R, split the stream and route one branch through an Am–Am combo
  (power divides).

## Open knobs
- None intrinsic — magnitude is set by the power routed in. (Balance lives in `Rmax` /
  `Pmax` / roll interval, defined in `../../vocab-overview/illusion.md`.)
