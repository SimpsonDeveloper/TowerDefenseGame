# First Primitive Op Behaviors

Roadmap item **4** — where compilation first *does something* visible in-game. Depends on
op-metadata flow (item 2, `../upgrades/op-flow.md`).

**Status: pipeline built, 4 of 7 primitives written.** A shot compiled from a lattice now lands on
an enemy and resolves. Burn, Corrode, Chill and Mind-damage are implemented. Of the rest, Mark
needs only numbers; Scramble waits on a shield system and Purify on an enemy-buff system, neither
of which exists.

| Primitive | Combo | Effect target | Behavior source | Built |
|---|---|---|---|---|
| Burn | Ru | HP over time | `../../effect-vocab/ops/primitives/burn.md` | ✅ |
| Chill → Freeze | Sa | slow, then halt | `../../effect-vocab/ops/primitives/chill-freeze.md` | ✅ |
| Corrode | Em | % max HP, in bouts | `../../effect-vocab/ops/primitives/corrode.md` | ✅ |
| Scramble | Ci | disrupt behavior | `../../effect-vocab/ops/primitives/scramble.md` | |
| Mind-damage | Am + Am | drain Mind | `../../effect-vocab/ops/primitives/mind-damage.md` | ✅ |
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
  → EnemyState                             stacks, flat states, Mind, queued damage
```

**A missing handler is a no-op, not an error.** Twenty-one ops are named and one is written, yet
any shot resolves end-to-end today. Nothing warns about the gap — "not built yet" is this table's
expected state for the whole of item 4.

## 2. The role interfaces

A primitive is **one class implementing the roles it needs**, so its numbers live in one file.
Chill implements all three; Burn and Corrode implement two.

- `IOp.Apply(context, quantity, target)` — what a *shot* does. `quantity` is the energy that
  crossed the producing edge, floored at 0 by the compiler. Per **edge**, not per gem: a ▽ with
  two inputs produces two ops.
- `ITickingState.Interval` / `.Tick(enemy)` — what a *carried state* does between shots.
- `IMovementModifier.SpeedScale(enemy)` — how a state changes movement. **Derived on every read,
  never stored**, so a state that expires stops slowing the enemy by not being there. Scales
  multiply, so two slows compound.

All register through the same `CombatRules.Add<T>`, which pattern-matches each role, and
`CombatRules.Default` is the shipped set. Adding a fourth role is one line there — the extension
point is more small interfaces, not a fatter one, so no op ever stubs out a face it does not
want.

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
- **Mind** — an innate meter, drained only. Item 5 (`enemy-mind.md`) gives it meaning.

Traffic across the boundary is **stats in, effects out**.

- **In** — `EnemyState.Vitals`, an `EnemyVitals` record of innate numbers: max HP, max Mind, base
  move speed. Ops read it because their curves are relative to the enemy (Chill's freeze threshold
  scales off max HP; Corrode ticks a percentage of it) and because a slow needs the unmodified
  speed to scale *from*. Set once in `EnemyStateComponent._Ready`, which is safe because a type is
  applied *before* the enemy enters the tree — `EnemyNavController.ApplyType`. Grow it by adding a
  parameter, and only when an op actually reads it.
- **Out, damage** — queued and pulled by `EnemyStateComponent` once a frame, since the core cannot
  see a `HealthComponent`. `HealthComponent.Hp` is a `double` so fractional ticks land as
  themselves; nothing is rounded or banked anywhere.
- **Out, movement** — `EnemyState.SpeedScale(rules)` is derived on demand and the component writes
  `base × scale` onto whatever implements `IMoveSpeed`, which both enemy controllers now do.
  Always from the base: scaling an already-scaled value compounds every frame.

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

An op needing private bookkeeping beyond a stack count does not get scratch space here — it gets
**its own `StateId`**. Corrode's bout is `StateId.Corroding`, whose stack count is the ticks
remaining, so it ends itself through the ordinary `TakeStacks` path and prints in the debug readout
unasked (`../../effect-vocab/ops/primitives/corrode.md`). The alternative, a scratch dictionary on
`EnemyState`, is the thing every later op would reach for first and nothing would ever see.

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

The one piece of this that **is** a game visual is the **Mind bar** — purple, stacked above the HP
bar, matching Amethyst because Amethyst is what drains it. It is a `StatBarComponent`, an abstract
two-rect bar that a subclass feeds a single fraction; the Shield bar will be another. Those bars
carry no text, so **colour is the label**, and stacking is just each node's `Offset` — no bar knows
what sits below it.

`StatBarComponent` is **pushed, never polled** — the same way `HealthBarComponent` works. A
subclass subscribes to a change event and calls `Refresh()`; no `_Process` runs, so an idle enemy
costs nothing a frame.

Two things make that possible, and both are worth keeping as the pattern:

- **`EnemyStateComponent.State` is built on construction, not in `_Ready`.** A sibling can then
  subscribe in its own `_Ready` whatever order the two run in, which is the same guarantee a
  `HealthComponent` gives its bar by being a node. Its `Vitals` arrive later, in `_Ready`.
- **`Mind` is derived from damage taken**, not stored as a running total, so vitals arriving after
  construction still leave the meter full. Assigning `Vitals` raises `MindChanged` too, since the
  meter is measured against `MaxMind`.

Worth having because states are otherwise invisible — Burn is a number in a dictionary bleeding a
fraction of a point a second, and without a readout the only evidence it works is an HP bar moving
slightly faster than expected.

## 7. Not done here

- **Real visuals.** Nothing renders a burning enemy: no tint, no particles. The broader visual
  language is unsettled, so this is deferred with the rest of it rather than guessed at now — the
  debug readout above is the stand-in.
- **The other three primitives.** Mark needs only a duration and a refresh rule, but its consumers
  (Focus, Detonate) do not exist, so it would be unverifiable beyond the readout. Scramble needs a
  **shield system** — a second bar, a per-enemy shield:HP split, and damage routing through it.
  Purify needs an **enemy-buff system**, which is undesigned.
- **Every interactive.** Several need machinery nothing has yet: multi-enemy reach for the arcs and
  Hex's death-spread, a targeting hook for Focus, a tick-rate role for Accelerant, and a
  state-removal event for Weather.
- **Balance.** `BurnTuning`, `CorrodeTuning`, `ChillTuning` and `MindDamageTuning` hold placeholders that make each shape legible, not tuned
  numbers. The tests pin their own values so retuning never turns them red.
