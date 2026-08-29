# Op: Burn

**Kind:** primitive — applies a state (self-consuming DoT)
**Applies:** Burn · **State merge:** sum stacks, **no cap**

---

## Definition

Ruby's ignition applies **Burn stacks** that tick HP damage and burn themselves away.

- **Damage is linear in the stacks standing at that tick.** The first tick is the biggest and
  every one after it is smaller — the burn fades as it eats itself.
- **Each tick spends stacks:** a flat base plus a fraction of the pile. The fraction is what
  keeps duration roughly flat however big the pile is; the base is what closes out the tail,
  since a fraction of 3 truncates to nothing.
- **No cap and no timer.** Stacks sum without limit; the burn ends when it has consumed itself.
  Stack count is therefore a clean read of the lattice that produced it.

The shape this buys: **a bigger pile burns harder, not proportionally longer.** 10× the stacks
costs only a handful of extra ticks, so investing in Burn scales damage rather than dragging out
the same damage over a longer clock.

## Numbers

Ported to `scripts/combat/core/ops/Burn.cs` as `BurnTuning`. This table is the source; the record
is the copy.

| Knob | Default | What it does |
|---|---|---|
| `StacksPerEnergy` | `1/20` | Edge energy → stacks, **rounded up**. Any energy at all is worth one stack. |
| `DamagePerStack` | `1.0` | HP per stack, per tick. |
| `DecayFraction` | `0.2` | Fraction of standing stacks burnt off per tick. |
| `DecayBase` | `1` | Stacks burnt off per tick regardless of size. |
| `TickInterval` | `1.0` | Seconds between ticks. |

Per tick: `damage = stacks × DamagePerStack`, then `spend = DecayBase + floor(stacks × DecayFraction)`.

`StacksPerEnergy` is calibrated against the **shipped starter turret**
(`assets/towers/turret_tower_def.tres` → `resources/crystal_templates/turret_starter.tres`):
core 150, Ruby·Ruby paying one Ruby's toll of 28, so **122 crosses the edge → 7 stacks**. That
burns for 5 ticks and 18 total HP per shot, before any stacking between shots.

```
7 → 5 → 3 → 2 → 1 → 0        18 HP over 5 ticks
```

Note `TowerDef.CoreEnergy` *defaults* to 600 but the shipped turret sets 150. Calibrate against
real tower defs, not the class default — a 600-core tower on the same lattice delivers 572 across
that edge, which is 29 stacks and 119 HP, a different weapon entirely.

```
29 → 23 → 18 → 14 → 11 → 8 → 6 → 4 → 3 → 2 → 1 → 0     119 HP over 11 ticks
```

**These are placeholders chosen to make the curve legible, not balance.** The tests pin their own
values (`tests/CrystalCore.Tests/BurnTests.cs`) so retuning here never turns them red.

## Status

**Built** — `scripts/combat/core/ops/Burn.cs`, one class covering both halves: `IOp` writing
stacks when a shot lands, `ITickingState` bleeding HP between shots. Pipeline notes in
`../../../impl-planning/combat/primitives.md`.

Not built: any real visual. `EnemyStateDebug` prints the stack count and flashes each tick, which
is enough to watch the curve above run for real, but nothing says "on fire".
