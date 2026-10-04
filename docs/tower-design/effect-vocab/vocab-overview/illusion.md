# Illusion resistance (Mind)

Part of the Effect Vocabulary overview — see `overview.md` for the index.

The maze is **psychological, not physical**. Enemies follow the player's set path
because they are fooled into thinking it is the only route.

- **Mind** = a second health bar ("mind shield"), per enemy, set at spawn. Drawn **purple**,
  stacked above HP (`../../impl-planning/combat/primitives.md` §6). Note it reads the opposite way
  round to every other bar: a **full** Mind is the dangerous case, an empty one is railroaded.
- Enemies **roll for deviation at set intervals**. Per-roll deviation chance is a
  function of current **Mind** (higher Mind → higher chance to break free).
- More time alive = more rolls = higher cumulative chance to deviate.
- **Frozen / inactive enemies skip rolls** (not attempting actions → no deviation).
- A deviating enemy leaves the path and shortcuts toward the target (skips the
  gauntlet → partial/full leak).
- **Mind-damage drains Mind. The break is permanent** — drive Mind to 0 and that enemy
  can never deviate again.

Design properties:
- **Two timers race:** kill-speed vs deviation-risk. Fast kills make illusion moot;
  tanky/long fights make it essential. Illusion is *inversely coupled to DPS*.
- **Placement role:** permanent break ⇒ apply illusion **early** (entrance gatekeeper).
- **Anti-turtle, free:** an enemy you can't kill eventually rolls a deviation and
  leaks. No infinite-CC cheese.
- **Map is the dial:** long maze → deviation skips a lot → illusion critical; short
  path → illusion near-worthless.
- Chill is currently **risk-neutral** (no damage tradeoff yet ⇒ same lifespan ⇒
  same roll count). Only matters for leak-bound enemies, not damage-bound ones.

Mind is drained by `mind`-typed power; how type routing works is in `damage.md`.

## Deviation math — f(Mind) (decided: linear ramp)

`f(Mind)` = per-roll chance the enemy breaks off the path, given its current Mind.

- **Shape: linear.** `p_roll = Pmax · (Mind / MindMax)`, where `MindMax` = spawn resistance,
  `Pmax` = deviation chance at a full shield.
- `f(MindMax) = Pmax` (fresh enemy, most likely to break); `f(0) = 0` (shield gone,
  permanently railroaded). Increasing in Mind.
- **Every point of mind-damage buys proportional safety** — no breakpoint, no wasted
  partial investment. Matters because split builds often deposit *small* mind amounts.
- **Rising risk over time is emergent, not built in:** a constant small `p_roll`
  still compounds across many rolls (`1 − (1−p)^n` grows with n), so longer-lived
  enemies accumulate deviation odds without any threshold.
- Rejected: **threshold flip** (`p = Pmax` until Mind < cutoff, then 0). Chipping does
  nothing until a cliff → punishes light Mind investment → clashes with the split economy.

Tuning knobs (values, not shape): `MindMax` per enemy type · `Pmax` · roll interval `T`.
