# First Primitive Op Behaviors

Roadmap item **3** — where compilation first *does something* visible in-game. Depends on
op-metadata flow (`../upgrades/op-flow.md`). Stub; flesh out when starting the phase.

Wire the seven primitives to an `EnemyStateComponent` the ops read/write:

| Primitive | Combo | Effect target | Behavior source |
|---|---|---|---|
| Burn | Ru | HP over time | `../../effect-vocab/ops/primitives/burn.md` |
| Chill → Freeze | Sa | ladder → skip enemy turns | `../../effect-vocab/ops/primitives/chill-freeze.md` |
| Corrode | Em | HP / armor over time | `../../effect-vocab/ops/primitives/corrode.md` |
| Scramble | Ci | disrupt behavior | `../../effect-vocab/ops/primitives/scramble.md` |
| Mind-damage | Am + Am | drain R | `../../effect-vocab/ops/primitives/mind-damage.md` |
| Purify | Qz | strip states (catalyst) | `../../effect-vocab/ops/primitives/purify.md` |
| Mark | Am + Ru | enabler flag | `../../effect-vocab/ops/primitives/mark.md` |

Plan when starting:

- `EnemyStateComponent` — ladders as int counters w/ thresholds (Chill→Freeze), flat states
  as bool+timer (Mark, Scrambled). R meter lives here too (`enemy-r.md`).
- `IOp.Apply(context, quantity, target)` strategy registry; one handler per `OpId`. Missing
  handler = no-op so the pipeline runs before all ops exist.
- Op quantity comes from the payload arriving at the output (`../upgrades/op-flow.md`).
- Two damage bars only: **HP** (kinetic default) and **R** (mind, Am–Am). No `ice` type.