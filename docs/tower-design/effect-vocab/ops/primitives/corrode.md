# Op: Corrode

**Kind:** primitive — applies a state (threshold DoT)
**Applies:** Corrode · **State merge:** sum stacks, **no cap**

---

## Definition

Emerald's rate applies **Corrode stacks** — acid that has to pool before it eats.

- **Threshold, then spend.** Stacks bank harmlessly until they reach `X`. At `X` the op spends
  `X` of them and opens a **bout**: **`Y`% of the enemy's max HP per tick, for 10 ticks.**
- **Leftovers stay banked.** Spending `X` off a pile of `X + n` leaves `n` toward the next bout,
  so sustained fire chains bouts instead of wasting the overflow. A bout that ends with `X` still
  banked opens the next one immediately, without waiting for another shot.
- **State merge:** two Corrode sources sum stacks. **No cap.**

This is a different shape from Burn on purpose. Burn is immediate and fades; Corrode is delayed
and flat. Burn rewards one big hit, Corrode rewards sustained ones — and because its damage is a
percentage of max HP, it is the answer to a health pool Burn's flat numbers cannot dent.

## Numbers

Ported to `scripts/combat/core/ops/Corrode.cs` as `CorrodeTuning`. This table is the source, the
record is the copy.

| Knob | Default | What it does |
|---|---|---|
| `StacksPerEnergy` | `1/20` | Edge energy → stacks, **rounded up**. |
| `BoutThreshold` | `10` | The `X`: stacks one bout costs. |
| `BoutTicks` | `10` | Ticks per bout. |
| `DamageFraction` | `0.02` | The `Y`: share of **max** HP eaten per tick. |
| `TickInterval` | `0.5` | Seconds between bout ticks. |

Against the shipped starter turret's 150 core, an Emerald·Emerald edge carries ~128 — about **7
stacks a shot**, so a bout opens every other shot and runs for 5 seconds taking **20% of the
enemy's max HP**, whatever that pool is.

**Placeholders chosen to make the shape legible, not balance.** The tests pin their own values
(`tests/CrystalCore.Tests/CorrodeTests.cs`).

## The bout is a state

A bout needs a countdown, and a countdown is not a stack count — but `EnemyState` carries no
per-op scratch space, on purpose, or every op would reach for it first.

So the bout **is** a state: `StateId.Corroding`, whose stack count is the ticks remaining. It ends
itself at zero through the ordinary `TakeStacks` path, its clock is dropped with it, and it prints
in the debug readout for free. Nothing consumes it and no combo writes it — bookkeeping wearing a
state's clothes — and that is the whole cost of the trick.

The ticker is registered against `Corroding`, not `Corrode`: the bank does not tick, it waits. The
threshold is checked where stacks can cross it — when a shot lands, and when a bout finishes.

## Status

**Built** — `scripts/combat/core/ops/Corrode.cs`, one class covering both halves: `IOp` banking
stacks and opening bouts, `ITickingState` running them. Pipeline notes in
`../../../impl-planning/combat/primitives.md`.

Not built: any visual. `EnemyStateDebug` prints `Corrode x7` and `Corroding x6`, which is enough
to watch a bout charge and discharge, but nothing says "dissolving".
