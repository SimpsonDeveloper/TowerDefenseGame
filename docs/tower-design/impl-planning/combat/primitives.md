# First Primitive Op Behaviors

Roadmap item **4** — where compilation first *does something* visible in-game. Depends on
op-metadata flow (item 2, `../upgrades/op-flow.md`).

**Status: pipeline built, 1 of 7 primitives written.** A shot compiled from a lattice now lands on
an enemy and resolves. Burn is implemented; the other six are registrations against the same two
interfaces.

| Primitive | Combo | Effect target | Behavior source | Built |
|---|---|---|---|---|
| Burn | Ru | HP over time | `../../effect-vocab/ops/primitives/burn.md` | ✅ |
| Chill → Freeze | Sa | ladder → skip enemy turns | `../../effect-vocab/ops/primitives/chill-freeze.md` | |
| Corrode | Em | HP / armor over time | `../../effect-vocab/ops/primitives/corrode.md` | |
| Scramble | Ci | disrupt behavior | `../../effect-vocab/ops/primitives/scramble.md` | |
| Mind-damage | Am + Am | drain R | `../../effect-vocab/ops/primitives/mind-damage.md` | |
| Purify | Qz | strip states (catalyst) | `../../effect-vocab/ops/primitives/purify.md` | |
| Mark | Am + Ru | enabler flag | `../../effect-vocab/ops/primitives/mark.md` | |

---

## 1. The pipeline

`scripts/combat/core/` is **engine-free**, on the same contract as the compiler core — the tests
project compiles it without `Godot.NET.Sdk`, so building at all is the proof it never grew a
`using Godot`. Only the two files directly above it need an engine.

```
TurretTower.Fire
  → ShotLanded (CompileResult, Node2D)     seam, unchanged since item 2
  → ShotDelivery.ToEnemy                   subscribed in TurretTower._Ready
  → EnemyStateComponent.Receive            the Godot half: clock + HP hand-off
  → ShotResolver.Resolve                   walks the ordered list, one op at a time
  → CombatRules.Op(id)?.Apply(...)         null = no-op
  → EnemyState                             stacks, flat states, R, queued damage
```

**A missing handler is a no-op, not an error.** Twenty-one ops are named and one is written, yet
any shot resolves end-to-end today. Nothing warns about the gap — "not built yet" is this table's
expected state for the whole of item 4.

## 2. The two interfaces

A primitive is usually **one class implementing both**, so its numbers live in one file:

- `IOp.Apply(context, quantity, target)` — what a *shot* does. `quantity` is the energy that
  crossed the producing edge, floored at 0 by the compiler. Per **edge**, not per gem: a ▽ with
  two inputs produces two ops.
- `ITickingState.Interval` / `.Tick(enemy)` — what a *carried state* does between shots.

Both register through `CombatRules.Add`, and `CombatRules.Default` is the shipped set.

## 3. What `EnemyState` owns — and what it deliberately does not

It holds **state and time. It holds no policy.**

- **Stacks** — `AddStacks` sums, and that is the whole rule. No cap, no lifetime, no decay. Those
  differ per op (Burn eats its own stacks; Corrode banks them to a threshold) and so they live in
  the op, with their numbers authored in `../../effect-vocab/ops/primitives/`.
- **Timed flats** — `SetFlag` keeps the longer timer (`../../effect-vocab/vocab-overview/merge.md`).
  A non-positive duration means a one-shot charge with no clock, ended by its consumer rather than
  by time (Brittle).
- **Consumers** — `TakeStacks` returns what it actually got, which is all a consumer may convert
  (1-to-1 for now, `../../effect-vocab/vocab-overview/states.md`). Taking the last stack ends the
  state and its clock.
- **R** — an innate meter, drained only. Item 5 (`enemy-r.md`) gives it meaning.

Traffic across the boundary is **stats in, damage out**.

- **In** — `EnemyState.Vitals`, an `EnemyVitals` record of innate numbers: max HP, max R. Ops read
  it because their curves are relative to the enemy (Chill's freeze threshold scales off max HP;
  Corrode ticks a percentage of it). Set once in `EnemyStateComponent._Ready`, which is safe
  because a type is applied *before* the enemy enters the tree — `EnemyNavController.ApplyType`.
  Grow it by adding a parameter, and only when an op actually reads it.
- **Out** — damage is queued and pulled by `EnemyStateComponent` once a frame, since the core
  cannot see a `HealthComponent`. `HealthComponent.Hp` is a `double` so fractional ticks land as
  themselves; nothing is rounded or banked anywhere.

**Current HP is deliberately not readable.** Max HP is a stat; current HP is a consequence, and an
op reading the bar back would make effects depend on the order damage happened to land in a frame.

## 4. Ticking

One clock, one call. `EnemyState.Tick` ages flat timers, then walks `CombatRules.Tickers` and
fires each whose state is active. `ITickingState.Tick(enemy)` is handed nothing but the enemy and
decides everything itself — damage, how many of its own stacks to spend, whether to end.

Four details are deliberate:

- **The loop walks the registered tickers, not the enemy's states.** A tick may write a second
  state (Frostburn ticks chill), which would invalidate an enumerator over what it is mutating.
- **A state with no registered op is carried but never ticks.** Same missing-handler rule as the
  op registry — it sits there readable by consumers rather than quietly expiring.
- **Clocks are per state and keep their phase.** Armed when the state first lands, so a fresh
  state waits one full interval before its first tick, and two ops on the same interval stay
  offset by when each arrived instead of snapping into lockstep.
- **The tick loop is a `while`, not an `if`.** One long frame owes more than one tick; dropping
  the extra would quietly make a lagging game cheaper for the enemy.

An op needing private bookkeeping beyond a stack count — Corrode's bout countdown — has nowhere
to put it yet. That is the next structural gap, not a numbers question.

## 5. Wiring an enemy

Add an `EnemyStateComponent` beside the enemy's `HealthComponent` and point its `Health` export at
it. Done for `scenes/enemy_nav_agent.tscn`. **Not** done for `scenes/enemy_raycast.tscn`, which has
no `HealthComponent` at all and so cannot be shot today. An enemy without the component takes the
gun's own damage and nothing else — unwired, not broken.

## 6. Seeing what is happening

`EnemyStateDebug` — a `Node2D` beside the HP bar — prints every carried state, its stacks or
remaining seconds, its countdown to the next tick, and the running total of damage states have
dealt. Each line flashes as its op ticks, driven by `EnemyState.Ticked`.

It is a development readout, not a game visual: `Enabled = false` unsubscribes it and it costs
nothing. Immediate-mode `_Draw` on a `Node2D`, so it never touches the UI theme.

Worth having because states are otherwise invisible — Burn is a number in a dictionary bleeding a
fraction of a point a second, and without a readout the only evidence it works is an HP bar moving
slightly faster than expected.

## 7. Not done here

- **Real visuals.** Nothing renders a burning enemy: no tint, no particles. The broader visual
  language is unsettled, so this is deferred with the rest of it rather than guessed at now — the
  debug readout above is the stand-in.
- **The other six primitives**, and every interactive. Chill and Corrode are designed —
  `../../effect-vocab/ops/primitives/chill-freeze.md`,
  `../../effect-vocab/ops/primitives/corrode.md` — and both are blocked on ops
  being able to see the enemy's HP.
- **Balance.** `BurnTuning`'s defaults are placeholders that make the curve legible, not tuned
  numbers. The tests pin their own values so retuning never turns them red.
