# Op: Hex

**Kind:** on-death trigger (system-consumer — the consumer is the death event)
**State:** Hexed (timed flag) · **State merge:** max timer (refresh)

---

## Definition

Amethyst stamps **Hexed** (timed flag). On death while Hexed, the enemy **spreads its
carried applied-states** to nearby enemies within a radius.

- Each state the dying enemy carries (Chill stacks, Burn, Corrode, Mark, …) is copied to
  each enemy in radius.
- A copied state merges into the recipient by that state's own state-merge rule
  (`../../vocab-overview/merge.md`): sum stacks (cap) / max timer / OR flag.
- Hexed itself is not spread — the copies are not hexed. One hop only, no chain reaction.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- Hexed enemy carrying **no** states → death spreads nothing (inert).
- Hexed + stacked DoTs → death **propagates** them to the pack.
- Death **without** Hex → normal, no spread.
- Each half is nothing without the other ⇒ interactive.

## Open knobs
- **Radius** — radial spread can be strong; tune / possibly cap.
- Whether **full or partial** stacks spread (all vs a fraction).
- Interaction with the per-state stack **caps**.
