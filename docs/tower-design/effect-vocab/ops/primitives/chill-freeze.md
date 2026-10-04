# Op: Chill → Freeze

**Kind:** primitive — applies a state (ladder slow → stop)
**Applies:** Chill stacks → Freeze · **State merge:** sum stacks, **no cap**

---

## Definition

Sapphire applies **Chill stacks**, each adding slow. Standalone.

- **Ladder** (slow → stop): stacks slow the enemy while they are held, down to a floor. Chill
  alone never stops anything — stopping is Freeze's job and has to be earned.
- **Past the threshold the enemy Freezes** — stops moving and skips deviation rolls.
- **Freezing spends the threshold.** The stacks that bought the freeze are consumed; overstack
  survives as credit toward the next one. Were they left in place the enemy would refreeze
  forever.
- **The threshold scales with the enemy's max HP.** A bigger enemy is harder to freeze, so the
  same pile that locks a small one merely slows a large one. This is Chill's defining asymmetry:
  Burn's damage is flat and Corrode's is proportional, while **Chill's cost is proportional**.
- **Stacks decay** when nothing is refreshing them, so a chill left alone wears off.
- **State merge:** two Chill sources sum stacks. **No cap.**

## Numbers

Ported to `scripts/combat/core/ops/Chill.cs` as `ChillTuning`. This table is the source, the record
is the copy.

| Knob | Default | What it does |
|---|---|---|
| `StacksPerEnergy` | `1/20` | Edge energy → stacks, **rounded up**. |
| `SlowPerStack` | `0.03` | Speed lost per stack held. |
| `MinSpeedScale` | `0.25` | Floor on the slow. |
| `FreezeThreshold` | `20` | Stacks to freeze an enemy of `ReferenceMaxHp`. |
| `ReferenceMaxHp` | `200` | The pool the threshold is quoted against; the real one scales from here. |
| `FreezeDuration` | `2.0` | Seconds frozen. |
| `DecayPerTick` | `1` | Stacks lost per tick. |
| `TickInterval` | `1.0` | Seconds between decay ticks. |

Threshold is `FreezeThreshold × MaxHp / ReferenceMaxHp`, rounded up and never below 1 — a trivially
weak enemy should not freeze on an empty hit.

Against the shipped starter turret's 150 core, a Sapphire·Sapphire edge carries ~134 — about **7
stacks a shot**, so a 200 HP enemy takes three shots to freeze and is slowed ~21% in the meantime.
A 1000 HP enemy needs 100 stacks and will mostly just be slow.

**Placeholders chosen to make the shape legible, not balance.** The tests pin their own values
(`tests/CrystalCore.Tests/ChillTests.cs`).

## Movement is derived, not stored

Chill is the first op that writes something other than damage, and it does it through a third role
alongside the shot and the tick: `IMovementModifier.SpeedScale(enemy)`
(`../../../impl-planning/combat/primitives.md` §2).

The scale is **recomputed from current states every frame and never stored**, so a chill wearing
off restores speed by not being there. Every active modifier's scale is multiplied together, so
slows compound rather than overwrite. `EnemyStateComponent` rewrites the owner's speed from the
base captured in `EnemyVitals.MoveSpeed` — always from the base, since scaling an already-scaled
value compounds every frame and an enemy chilled once would crawl forever.

`Frozen` is a second, tiny modifier returning 0, keyed on `Freeze`. It is separate from `Chill`
because a modifier is keyed by the state it reads, and these are two states. It carries no `IOp`
face: nothing produces Freeze but Chill crossing its threshold.

## Status

**Built** — `scripts/combat/core/ops/Chill.cs`. Three roles in one class (shot, tick, movement),
plus `Frozen` for the halt.

Not built: **skipping deviation rolls**, the other half of what freezing means — deviation itself
is roadmap item 5 (`../../../impl-planning/combat/enemy-r.md`). And no visual; `EnemyStateDebug`
prints `Chill x7` and `Freeze 1.4s`.
