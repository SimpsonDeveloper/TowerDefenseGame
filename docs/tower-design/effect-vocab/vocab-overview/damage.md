# Damage types & bars

Part of the Effect Vocabulary overview — see `overview.md` for the index.

Type is a **routing tag on one power magnitude**, not a separate stat channel.

- `power` = magnitude. `type ∈ {kinetic, mind}` = which bar it deposits into.
- `kinetic` → **HP** (the default). `mind` → **R** (illusion resistance — see `illusion.md`).
- The tag only **picks the bar** — it never changes the magnitude.
- **Only an Am–Am combo** converts `kinetic → mind`: two amethyst crystals in sequence
  (the Mind-damage native op) reroute that power to R. A lone amethyst does nothing.
- Example: a 50-power stream run through an Am–Am combo → 50 mind-damage to R.
- **Type is single-valued per stream.** A shot hits HP *or* R, not both.
- **There is no `ice` type.** Sapphire's ice flavor is a *state* (Chill → Freeze
  stacks), not a bar it deposits into — see `states.md`.

Enemies may also carry a **Shield** — a second HP bar in front of HP (not R) that must
be dropped first. That mechanic lives in `shield.md`.

## Hitting both bars at once

Allowed, not free. Two conservation-safe ways:

1. **Split (decided default).** Split the flow → two branches; route one through an
   Am–Am combo (`mind`), leave one `kinetic`. Power **divides** across them — both bars
   hit, each weaker. The
   **split ratio** is the build lever (90/10 finisher that chips R; 20/80 controller).
2. **Hybrid crystal (deferred).** One cell that internally splits its stream (e.g.
   60% HP / 40% R). Same total power, ratio baked in; trades crafting material for
   the lattice space a manual split costs. Designed later.

**Forbidden:** an *undivided* 50-power shot depositing 50 to HP **and** 50 to R =
100 output from 50 power = conservation break = free double damage.
