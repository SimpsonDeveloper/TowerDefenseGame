# Delivery (owned by tower type)

Part of the Effect Vocabulary overview — see `overview.md` for the index.

Delivery is a **separate axis** from crystal effects. A tower has a **type** that
fixes how its hit lands; the crystals fix what the hit does. Combining the two is
the build.

- Tower type = the delivery: single-shot, **nova**, **splash**, **field**, **arc**,
  **beam** (list not final).
- Delivery is **state-blind** — a function of impact position only. It never reads
  Burn/Freeze/R. Only crystal ops write state; only consumers read it.
- A delivery may read live position (e.g. nova measures how many enemies are near
  the impact) — that is a **query at hit-time**, not an authored state on the enemy.
  Density is computed, never stamped. This is why "Clustered" is not a status.
- Some consumers only pay off under a matching delivery; the effect is authored with
  the crystals, the shape is supplied by whatever tower type carries them. Mismatch =
  the effect is inert, not illegal.

Detailed delivery vocabulary (shapes, ranges, tower-type roster) is out of scope
for this doc — it lives with the tower-type design.
