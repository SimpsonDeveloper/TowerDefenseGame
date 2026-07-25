# Shield (defensive layer)

Part of the Effect Vocabulary overview — see `overview.md` for the index.

A **Shield** is a **second HP bar** sitting in front of an enemy's HP — it must be
depleted before HP takes damage. Each enemy carries a **shield : HP split** on a
spectrum: from **no shield** (pure HP, ratio 1:0) up to **shield-heavy** — but never pure
shield (every enemy has some HP behind it).

Two ways to drop the shield, both producing the **Shield-down** state:

- **Break by damage** — chip the shield bar to 0 with HP damage. The shield's worth of
  HP is real damage you must pay.
- **Scramble** — disable it directly, no damage needed (`../ops/primitives/scramble.md`).
  Skips paying the shield bar down.

While Shield-down, HP damage lands unblocked. Shield does **not** protect R —
mind-damage routes to R regardless (armor, not a mind defense; see `damage.md` and
`illusion.md`).

## Scramble's tradeoff

Because the shield fraction varies per enemy, running Scramble is a **bet on the wave**:

- **Shielded enemy** → Scramble skips the whole shield bar → strong tempo win.
- **HP-only enemy** → nothing to disable → the crystal effect is **wasted**.

So it is a counter, not a tax — its value scales with how shield-heavy the wave is.

Shield-down is also a **consumer hook** — a home for future combo ops (bonus / execute
vs shield-down); those cells are reserved until authored.
