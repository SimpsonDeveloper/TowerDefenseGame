# Crystal Tower — Energy Conservation

How energy flows through a crystal lattice and powers each crystal's combo-op.
Prototyped in `playground/dataflow-playground.html` (`compile()`). Supersedes the earlier
"global draw" model.

---

## The rule

Energy is a **conserved, consumable multiplier**. It is not a pool taxed once up
front — it is spent **locally**, one crystal at a time, as the stream flows.

1. **Seed.** The **core energy** `E_core` is divided **equally** among the sources
   (no weights): each of `n` sources is seeded `E_core / n`. Sources are automatic —
   every leaf-input crystal is one (`compilation-system.md` §2).
2. **Local toll.** As the stream passes a crystal, that crystal **draws its own
   cost** from the flowing energy: `out = in − cost`.
3. **Split (▲)** divides the **post-toll** energy among its outputs.
   **Merge (▽)** sums the energy of its inputs.
4. **Floor at 0 for use.** Energy at or below 0 delivers nothing — see *Debt*.

Non-energy stats (rate, speed, range, slow) still route by plain conservation
(▲ divides, ▽ sums); only **energy** carries the per-crystal toll.

---

## Energy is the combo-op multiplier

Adjacent crystals in a chain form a **combo** (see
`effect-vocab/vocab-overview/combo-matrix.md`). The combo's magnitude is the
**energy arriving at the second crystal** of the pair, floored at 0.

For a chain `a → b → c`, the combo `(a,b)` is scaled by the energy entering `b`;
the combo `(b,c)` by the energy entering `c`. Because each crystal tolls its cost
before passing energy on, **each successive combo fires on less energy** — the
front of the chain is the strongest.

### Worked example

`E_core = 20`, chain `a → b → c` with costs `1, 2, 3`:

| crystal | energy in (= combo multiplier) | toll | energy out |
|---|---|---|---|
| a | 20 | −1 | 19 |
| b | **19**  ← scales combo (a,b) | −2 | 17 |
| c | **17**  ← scales combo (b,c) | −3 | 14 |

The weapon collects the chain's exit energy: **14**.

Split the chain instead (`a → {b, c}`): `a` outputs `19`, divided → `9.5` each;
then `b` tolls to `7.5`, `c` to `6.5`. Splitting divides the **already-tolled**
energy, so it costs far less advantage than paying the full draw on every branch.

---

## Debt

A crystal that receives energy ≤ 0 is in **debt**: its combo-op multiplies by 0
(does nothing). The negative value **keeps flowing** — it is not clamped to 0 in
transit — so a downstream **merge (▽)** can sum a debted branch against a positive
one and bring the stream back above 0. Debt is an obstacle to overcome, not a dead
end.

---

## Consequences

- **Front-loaded energy.** Early crystals fire on the most energy; deep ones on
  the least. **Crystal order is a strategy axis** — place the combos you want
  strongest first.
- **Cost is local.** Adding a crystal only bleeds energy **downstream** of it;
  crystals ahead of it are unchanged. Build size does not tax itself everywhere.
- **Splitting stays viable.** Breadth divides post-toll energy at a mild, stable
  rate instead of re-paying the whole draw per branch.
- **Depth self-limits.** A chain runs into debt once accumulated tolls exceed
  `E_core`; the tail goes inert first, the front keeps firing. No arbitrary cap.

---

*See `compilation-system.md` for the surrounding pipeline and `legend.md` for
symbols.*
