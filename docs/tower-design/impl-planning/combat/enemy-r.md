# Enemy R System + Paths

Roadmap item **5** — the largest scope. Depends on primitive ops (`primitives.md`). Stub;
flesh out when starting the phase. Design source: `../../effect-vocab/vocab-overview/illusion.md`.

The R (illusion-resistance) mechanic is not just a second health bar — it changes **pathing**,
which does not exist yet. This item bundles several sub-systems:

1. **R meter** — per-enemy "mind shield", set at spawn, drained permanently by mind ops
   (Am–Am / Mind-damage). Lives on `EnemyStateComponent`.
2. **Preset enemy paths** — enemies follow an authored route (does not exist yet).
3. **Player-placed roads** — the player lays the route; integrates with the tower placement
   system (roads are placeable like towers). This is what enemies follow / deviate from.
4. **Deviation** — enemies roll at intervals; per-roll break chance = `f(R)` (linear
   ramp). Frozen/inactive enemies skip rolls. R → 0 = permanently railroaded.
   Deviating enemies shortcut toward the target (partial/full leak).

Sequencing note: 2 + 3 (paths + roads) are prerequisites and sizeable on their own — likely
split into their own sub-items before R deviation is wired. Expect this to be broken down
further when reached.