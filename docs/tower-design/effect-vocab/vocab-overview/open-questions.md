# Open questions (v2)

Part of the Effect Vocabulary overview — see `overview.md` for the index.

- **Frostburn** conversion ratio `r` (fire tick → chill stacks): tuning open.
  (`../ops/interactives/frostburn.md`)
- **Shatter** coefficients: `Cshatter` (per-stack burst) · `Nbrittle` (Brittle's flat
  charge) · `Tfreeze` · optional cap. (`../ops/interactives/shatter.md`)
- **Enemy buffs** (undefined) — candidate set: +% move-speed · +% HP · +% attack ·
  +% Mind (mind shield). **Purify** (quartz) strips them; define Purify fully once the
  buff system exists.
- **Shield-down consumers** — bonus / execute-vs-shield-down ops are a good use for
  reserved combo cells (populate the matrix); author as they earn a triad. (`damage.md`)
- **Hex spread balance** — spread radius (radial dump can be strong), full-vs-partial
  stack spread, interaction with per-state caps. (`../ops/interactives/hex.md`)
- **Reserved combo cells** — fill only as new ops earn them. (`combo-matrix.md`)

---

## Settled

Decisions taken while building the primitives (roadmap item 4,
`../../impl-planning/combat/primitives.md`). Recorded here because each one closed a question that
was open above.

- **State-merge caps: there are none.** Stacks merge by plain sum, uncapped, for Chill, Burn and
  Corrode alike. What stops a pile being dangerous forever is the op's own **decay or spend curve**,
  authored per op, not a ceiling in the merge rule (`merge.md`, `states.md`). This keeps stack count
  an honest read of the lattice that produced it.
- **Charge-and-spend ops spend their threshold.** Corrode's bout and Chill's freeze both consume
  the stacks that bought them, leaving the overstack as credit toward the next one
  (`../ops/primitives/corrode.md`, `../ops/primitives/chill-freeze.md`). Leaving them in place
  would refire forever off one purchase; clearing the lot would waste sustained fire.
- **An op's private bookkeeping is a state, not scratch space.** A counter an op needs for itself
  gets its own `StateId` whose stacks are the count — Corrode's bout is `Corroding`. It then ends
  itself through the ordinary merge-and-spend path and shows up in the debug readout, where a
  hidden field would not. These are **implementation states**: no producer, no consumer, and they
  do not belong in the table in `states.md`.
