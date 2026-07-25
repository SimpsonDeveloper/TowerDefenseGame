# Op: Weather

**Kind:** reactive counter (stores freeze-removal count)
**Reacts to:** Freeze removal

---

## Definition

Weather tracks a per-enemy **freeze-count**: each time the enemy's **Freeze is removed**
(by Shatter or any means), the count increments. The enemy then takes **escalating
damage scaled by that count** — repeated freezing weathers its structure like freeze-thaw
cracking rock. The count only grows; escalation is capped so it can't run away.

The freeze-count is a per-enemy **counter/mechanic** (like R), not a written-and-consumed
state.

## Interactive

Passes the interactive test (canonical definition:
`../../vocab-overview/overview.md` → *The interactive test*).

- No **Freeze cycling** → the count never grows; no escalation.
- Remove **Weather** → freezes come and go with no lasting toll.
- Together: each freeze-removal matters only because Weather counts it, and the count is
  worthless without freezes to tally ⇒ interactive.

## Open knobs
- Escalation multiplier per count · the **cap** on the multiplier · whether the count
  decays over time or persists for the enemy's life.
