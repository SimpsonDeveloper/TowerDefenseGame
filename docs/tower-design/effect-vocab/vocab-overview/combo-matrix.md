# Crystals & combos → ops

Part of the Effect Vocabulary overview — see `overview.md` for the index. **This is the
source of truth for which crystals produce which ops.** Op prose (formulas, interactive
proofs, open knobs) lives one-per-file under `../ops/`.

## Roster

Each crystal has an **element** — a flavor label (no mechanics of its own) that was the
*inspiration* for what kind of op a crystal earns.

| Crystal | Element |
|---|---|
| **Ru** Ruby | Fire |
| **Sa** Sapphire | Ice |
| **Em** Emerald | Nature / Acid |
| **Ci** Citrine | Lightning |
| **Am** Amethyst | Arcane / Mind |
| **Qz** Quartz | Pure |

## The matrix (N × N)

The full grid over the six crystals. Cells are **unordered pairs** — direction does not
matter yet, only the combo; the grid is symmetric across the diagonal.

- **Diagonal** (same crystal) = that crystal's **single-crystal native** op(s).
- **Off-diagonal** = a **two-crystal** op.

Grid is fully assigned — every pair earns an op.

|  | Ru | Sa | Em | Ci | Am | Qz |
|---|---|---|---|---|---|---|
| **Ru** | Burn | Frostburn | Accelerant | Fire Arc | Mark | Flareup |
| **Sa** | Frostburn | Chill → Freeze | Weather | Frost Arc | Numb | Shatter |
| **Em** | Accelerant | Weather | Corrode | Acid Arc | Hex | Dissolve |
| **Ci** | Fire Arc | Frost Arc | Acid Arc | Scramble | Detonate | Short-circuit |
| **Am** | Mark | Numb | Hex | Detonate | Mind-damage | Focus |
| **Qz** | Flareup | Shatter | Dissolve | Short-circuit | Focus | Purify |

Reading the matrix:
- **Diagonal natives:** Ruby = Burn · Sapphire = Chill → Freeze · Emerald = Corrode ·
  Citrine = Scramble · Amethyst = Mind-damage · Quartz = Purify.
- **Amethyst is the mind hub:** all its arcane ops pair off it — Mark (Ru), Hex (Em),
  Focus (Qz), Detonate (Ci), Numb (Sa), Mind-damage (Am+Am native — two in sequence).
- **Citrine is the conduction hub:** Fire / Frost / Acid Arc (with Ru / Sa / Em) are
  chain lightning that jumps between burning / chilled / corroded enemies.
- **Quartz is the stack-spend catalyst:** a `Quartz + X` combo spends X's ladder (the
  charge-and-spend shape, see `states.md`).

## Ops → files

| Op | Class | Combo | File |
|---|---|---|---|
| Burn | primitive | Ru | `../ops/primitives/burn.md` |
| Chill → Freeze | primitive | Sa | `../ops/primitives/chill-freeze.md` |
| Corrode | primitive | Em | `../ops/primitives/corrode.md` |
| Mark | primitive | Am + Ru | `../ops/primitives/mark.md` |
| Scramble | primitive | Ci | `../ops/primitives/scramble.md` |
| Mind-damage | primitive | Am | `../ops/primitives/mind-damage.md` |
| Purify | primitive | Qz | `../ops/primitives/purify.md` |
| Frostburn | interactive | Ru + Sa | `../ops/interactives/frostburn.md` |
| Shatter | interactive | Qz + Sa | `../ops/interactives/shatter.md` |
| Flareup | interactive | Qz + Ru | `../ops/interactives/flareup.md` |
| Dissolve | interactive | Qz + Em | `../ops/interactives/dissolve.md` |
| Detonate | interactive | Ci + Am | `../ops/interactives/detonate.md` |
| Focus | interactive | Am + Qz | `../ops/interactives/focus.md` |
| Hex | interactive | Am + Em | `../ops/interactives/hex.md` |
| Fire Arc | interactive | Ru + Ci | `../ops/interactives/fire-arc.md` |
| Frost Arc | interactive | Sa + Ci | `../ops/interactives/frost-arc.md` |
| Acid Arc | interactive | Em + Ci | `../ops/interactives/acid-arc.md` |
| Numb | interactive | Sa + Am | `../ops/interactives/numb.md` |
| Accelerant | interactive | Ru + Em | `../ops/interactives/accelerant.md` |
| Weather | interactive | Sa + Em | `../ops/interactives/weather.md` |
| Short-circuit | interactive | Ci + Qz | `../ops/interactives/short-circuit.md` |

Op index (files, no combos): `../ops/README.md`.
