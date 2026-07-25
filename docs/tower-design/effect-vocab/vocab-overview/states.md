# States

Part of the Effect Vocabulary overview — see `overview.md` for the index, core
principle, and the triad definition.

A state-transition graph. Each row is one state; **Producer** and **Consumer** name
what writes and reads it (a crystal, a state, or an op) — states chain into each other.
Ladder shape lives in the op file; merge rules in `merge.md`; the non-additive proof in
each interactive op's **Interactive** section.

| State | Producer | Consumer |
|---|---|---|
| Chill | Sapphire | Freeze |
| Freeze | Chill (past threshold) | Shatter |
| Burn | Ruby | Flareup |
| Corrode | Emerald | Dissolve |
| Brittle | Dissolve | Shatter |
| Mark | Amethyst + Ruby | Focus, Detonate |
| Shield-down | Scramble, damage-break | (HP damage bypasses shield — `shield.md`) |
| Hexed | Hex | death-spread (`../ops/interactives/hex.md`) |

**Illusion resistance (R)** is a **meter/mechanic, not a state** — an innate second HP
bar drained by mind-damage, not something written and consumed. It lives in
`illusion.md`.

## Reusable shapes

- **Charge-and-spend** — a **ladder** producer (Chill / Burn / Corrode) paired with
  a consume-the-stack verb (Shatter / Flareup / Dissolve). This is the core engine.
  Quartz is the **stack-spend catalyst** (no effect of its own); the spend verb is
  always `Quartz + [the ladder's element crystal]`:

  | Ladder (producer) | Spend op | Combo |
  |---|---|---|
  | Chill → Freeze | Shatter | Quartz + Sapphire |
  | Burn | Flareup | Quartz + Ruby |
  | Corrode | Dissolve | Quartz + Emerald |

  Magnitudes and interactive proofs live in each op file under `../ops/interactives/`.
- **Flat states** — a single on/off flag (no ladder). Two sub-shapes, same wording:
  - *Persistent gate* — stays on until its timer ends; gates other effects while up
    (Mark, Scrambled, Hexed).
  - *One-shot charge* — a gate that a consumer spends and clears (Brittle: Shatter
    eats it). Functionally a self-disabling gate; not a stacking ladder.

## Many-to-many

Producers and consumers are **many-to-many** — keeps the authored set small:
Shatter consumes Freeze **and** Brittle; Death consumes Hex. One consumer can read
several states; one state can feed several consumers.

Example: **Shatter** is fed by two independent ladders (ice → Freeze, acid → Brittle) —
one consumer, two states. Detail in `../ops/interactives/shatter.md`.

## State coupling (how ops interact without touching)

An op writes a flag on the enemy; another op reads it later. The reader can be a
**different tower**, or the same tower at a later time. State is the channel where
ops that are *not* neighbors interact (Tower 1 freezes, Tower 2 shatters). Writing a
state is uniform; reading is what differs by consumer.