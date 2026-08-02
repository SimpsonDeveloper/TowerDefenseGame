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
| Burn | Ruby | Flareup, Frostburn |
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

## Shot resolution (applying the ordered op list at hit time)

**Consumers are resolved on the enemy, not in the compiler.** The compiler emits an
**ordered list** of `(op, quantity)` (`../../impl-planning/upgrades/op-flow.md`); this is
where that list is spent.

When a shot lands, the enemy walks the list **in order** (see below) and applies each op **one
at a time**, mutating enemy state. Each op reads the enemy's state **as it is at that step** —
so an op's effect depends on what earlier ops in the same shot already wrote. This is the
runtime half of the triad: producers wrote the ordered list; consumers spend it here.

- **Order** (fixed at compile, from lattice geometry): **vertical first** — lower gems first,
  so a higher gem is always applied **last**; **horizontal second** — leftmost first; anchored
  to each op's producing (downstream) gem (`../../impl-planning/upgrades/op-flow.md` §3).
- **Consumption is 1-to-1 (for now).** When a consumer eats a state it converts it at the same
  quantity; per-op ratios are authored later (`../ops/interactives/`).
- **All in one frame.** The whole list resolves within the hit's single frame — the player
  never sees the intermediate states between ops; only the net result after the last op.

**Worked example — Frostburn after Burn.** A shot carries `[Burn ×n, Frostburn ×m]` (Burn
lower, Frostburn higher, so Burn is applied first). At hit time:

1. **Burn** adds `n` Burn stacks (true fire — ticks HP).
2. **Frostburn** reads the current Burn and does an **initial consume**: the Burn is spent
   (removed) and converts **1-to-1** into **Frostburn** stacks — a retained state. Frostburn
   stacks then **tick over time**, applying **chill** on each tick (as Burn ticks HP, Frostburn
   ticks chill), driving the enemy toward Freeze. Full behavior in
   `../ops/interactives/frostburn.md`.

Net after the frame: the enemy carries Frostburn stacks — a state no single op in the list
writes alone. Reorder the same two ops (Frostburn before Burn) and Frostburn finds no Burn to
convert and is inert — which is exactly why the order is fixed by the lattice.