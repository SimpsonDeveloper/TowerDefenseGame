# Player-defined paths — wards

Roadmap item **5**, sub-item 3 (`enemy-mind.md` → *Player-placed roads*). Depends on nothing in
item 4; it is the half of item 5 that can be built before the Mind deviation it exists to serve.

**Status: placement, routing, preview, collision with towers, and enemies walking it built.
Deviation is not.**

---

## 1. A ward is a waypoint, not a road tile

The player places **wards** — discrete markers, one per tile — and the path is the route through
them in placement order. A ward is a **navigation destination**, and that is the whole of what it
is: enemies already path over a baked mesh, so going ward-to-ward needs no pathfinding of its
own. The existing nav walks the ground between two wards; the player only says
which points to visit.

Three things fall out of that, and they are the reason for the choice:

- **No new pathing code.** A route is a `List<Vector2I>` of tiles. Nothing traces corridors,
  nothing autotiles, nothing re-bakes.
- **Deviation is already nearly free.** An enemy that breaks the illusion (`../../effect-vocab/vocab-overview/illusion.md`) stops
  targeting its next ward and targets the core instead — one assignment, not a system. That is the
  whole point of item 5 and it should not cost a pathing rewrite.
- **Terrain stays authoritative.** Water and rock have collision and the nav mesh already knows,
  so the route bends around them with no rule of its own.

The rejected alternative was painting a road tile-by-tile, with autotiled corners. It costs a 13+
piece tileset, a corridor data structure, and a rule for what happens when a road crosses water —
and it buys a look the game does not want, for reasons in §2.

The route object is `MiragePath` (`scripts/world/paths/`) and the markers on it are **wards**. The
naming follows the mechanic rather than the art: the wards are real things the player placed, and
the path between them is the thing that is not there.

### The core is point 0

The route is **anchored at the core**, which is an implicit first point the player never places
and cannot lift. A route has to end somewhere and it always ends there, so making it a ward would
only invite laying one that does not. Wards extend the route *outward* from the core, and an
enemy walks the list backwards — picking it up at the far end and following it inward.

Two things fall out for free: a single ward already draws a leg, and the hover preview has
something to route from before anything is placed, so the first ward shows its leg instead of
appearing out of nowhere.

## 2. The look: ward sigils

**The maze is psychological, not physical** (`../../effect-vocab/vocab-overview/illusion.md`).
Enemies walk the route because they are fooled into thinking it is the only one. A cobbled road
would contradict that: if the road were real, deviation would be impossible, and deviation is the
mechanic. So the path is drawn as something the player *cast*, not something they built.

Chosen after comparing five treatments on the real terrain colours:

| Part | Decision |
|---|---|
| **Glyph** | **Ring** — an 8×8 hollow square with a 2×2 lit core |
| **Thread** | dotted, 2×2 pips every 6px, drifting toward the core |
| **Minor spacing** | an unlit ring every **4 tiles** along the thread |
| **Outline** | 1px dark ring either side of the purple — not optional |
| **Motion** | slow pulse on the core, offset per ward |

Why the ring, specifically: the terrain is four sub-tile colour variants at 4px blocks, which is
visually loud. **Noise makes blocks, never outlines** — so a hollow square is the one shape the
ground cannot imitate, and it stays legible where a filled glyph of the same size turns to mush.
Hollow also means the mark never hides the tile under it, which matters on a map being read for
tower spots. It is deliberately placed at odd pixel offsets so its edges do not land on the 4px
noise boundaries.

Purple is `#a974ff`, the same fill as the Mind bar. The path is a Mind attack, so it borrows the
meter's colour rather than inventing one. Nothing glows: the game has no bloom and every sprite in
it is flat with a hard dark outline, so the sigils are too.

### Major and minor rings

A ring with a **cyan core** is a ward the player placed. A ring with **no core** is drawn
automatically every 4 tiles along the thread. The distinction is free to render and tells the
player which marks they can remove.

## 3. What is drawn is what would be walked

Each leg between two wards is a real `NavigationServer2D.MapGetPath` query — the same one an
enemy's `NavigationAgent2D` makes — so the thread bends around a tower exactly where an enemy
would. A straight line between wards would be a drawing that lies: it would cross towers and
water, and the player would lay a route by it that nothing ever takes.

### The pocket is one nav region

Legs used to kink for no reason. Godot's path query is A* over nav *polygons*, then a funnel that
pulls the line tight inside the strip of polygons A* chose. The funnel is exact; the strip is not.
The pocket was baked as a grid of separate 512px regions, each triangulated on its own, and a
diagonal leg between two regions that touch only at a corner went round through a third, so the
thread bent at the vertex where four regions meet.

The fix is at the source: `CoordConfig.NavCellSizeChunks = 4`, one region over the whole 4×4-chunk
pocket, so there are no seams to bend at. The grid existed so that placing a tower rebakes only one
cell; a whole-pocket rebake measured **0.2–0.6 ms** with a dozen turrets down, so it bought nothing.

Tried first and removed: a line-of-sight pass over the raw path. It worked, but it patched a
symptom of the seams and cost enemies their `NavigationAgent2D`, since the agent cannot be handed a
path. With one region the raw path is straight and the pass has nothing to do. If bends ever
reappear inside a single region (Godot's A* is not exact with large triangles), that is the
technique to bring back, and the first thing to check is whether the mesh has been split again.

A leg with **no way through** is drawn as a bare red dash straight across. It is not a route and
must not look like one; `IsBroken` says so in code.

The nav mesh bakes **asynchronously** after a tower lands, so there is no single moment at which
recomputing once would be correct. The route re-queries on a slow interval (0.25s) as well as on
every edit — honest about the asynchrony, and a few path queries per second is nothing.

## 4. Towers and wards refuse each other

Both directions, because either one alone leaves a hole:

- **A ward cannot go on a tower's tile**, or on unwalkable ground. A tower cuts the nav mesh away
  beneath itself, so a waypoint inside one is a destination no enemy can stand on.
- **A tower cannot go on the route** — not just on a ward, but anywhere the walked thread crosses.
  Dropping one across the thread would force a detour the player never chose. The ward is the
  instruction and the tower does not get to overrule it, so the tower ghost turns red there.

Tested by segment-versus-cell clipping against the *routed* polyline, so it follows the thread
around corners rather than testing a straight line.

## 5. Placement

Key **4** on `TowerPlacementManager`, after Pan / Turrets / Edit (1–3). Same clicks as turret
mode: the left button makes the thing, the right unmakes it.

| Input | Effect |
|---|---|
| Hover | preview: the ring where it would land, and the leg it would add, routed for real |
| Left-click an empty tile | append a ward to the end of the route |
| Right-click a ward | lift it; its neighbours join up |
| 1 / Escape | back to Pan |

The preview routes rather than guessing, so the player sees the thread bend around a tower
**before** committing. A tile that cannot take a ward previews red and a click on it does nothing.

Wards are free and unlimited for now. There is no cost model because there is no economy to
charge against yet, and guessing one would be a number nobody can tune.

Removing a middle ward rejoins the route rather than splitting it, because a path with a hole in
it has no meaning — there is exactly one route, and it is ordered.

## 6. Balancing an unbounded maze — open

Wards are free and unlimited, so a player can lay a hundred-ward hairball. **Noted, not solved.**

Against enemies with Mind intact this is already self-punishing and needs no rule: more time alive
means more deviation rolls, `1 − (1−p)^n` climbs, and a deviating enemy shortcuts to the core,
so a huge maze leaks nearly everything
(`../../effect-vocab/vocab-overview/illusion.md`). The maze punishes itself.

**The hole is Mind-0.** Drain Mind at the entrance — which the illusion design explicitly
recommends as the placement role — and every enemy is permanently railroaded, deviation never
fires, and the hairball becomes a free infinite gauntlet. The exploit is the intended strategy
taken to its limit, and no deviation-based lever can reach it.

Candidates, in rough order of preference:

| | Idea | Binds a railroaded run? |
|---|---|---|
| **A** | **Wards cost resources** — finite, and maze length competes with towers | yes |
| **E** | **Ward upkeep** — each ward bleeds resource per wave | yes |
| **F** | **Plausibility** — a leg heading away from the core is harder to believe, so it costs more hold; a switchback maze stays cheap, a hairball does not | yes |
| **B** | Roll for deviation per distance walked rather than per second | no |
| **C** | A fixed illusion budget spread over the route, so `Pmax` rises with length | no |
| **D** | A hard length cap, shown as a counter | yes, but a visible leash |
| **G** | Forbid self-crossing legs | shape only |

**Leaning A + E** — the cost side. Wards being free is the real anomaly: nothing else in the game
is, and the economy (`Inventory`, `TowerDef.Cost`) is already there to charge against.

The decision that gates the rest: **is a Mind-0 enemy unconditionally railroaded?** If yes, only
the cost levers can ever bind. If an absurd enough route can still be broken, **F** becomes the
whole answer and the cost is optional.

## 7. The core, and where enemies go without a route

A route needs somewhere to lead, so the **Core** exists before deviation does
(`scripts/world/structures/Core.cs`). One per map, placed by the map rather than the player. A
`StaticBody2D` on the tower layer carrying a `HealthComponent`, registering a footprint through
the same `TowerFootprintTracker` towers use — so enemies path to it, the nav bake carves around
it, and a turret cannot be dropped on top of it.

**192×192**, against the turret's 128: the core should read as the biggest thing on the map, and
a structure's footprint is its collision is its art, so all three are that number. 2000 HP, a
placeholder until there is something to lose.

Groups: **`Structures`** (towers are in it too — it is what an enemy's attacker hits) and
**`Core`** (how anything finds it). Deliberately *not* `Towers`, which means "a thing the player
built and can edit" and which half the tower systems assume.

### Default behaviour: head for the core, chew through what is in the way

`CoreTargetingPattern`, now the default on the enemy variant. It builds the **same**
`EnemyStructureTargeter` the tower pattern builds, pointed at the `Core` group instead of
`Towers` — that is the whole difference between the two behaviours.

### When a turret becomes the objective

Three states, and which one an enemy is in is the whole rule:

| State | Behaviour toward turrets |
|---|---|
| **Free** — no route laid for it | breaks off to kill any turret whose edge comes inside `AggroRadius` |
| **Railroaded** — walking a route it believes in | walks straight past, whatever it passes |
| **Deviating** — broke the illusion | free again; a turret beside it becomes the objective |

So proximity aggro is gated on the enemy being free, and **breaking the illusion is what lets an
enemy turn on the guns**. That is the payoff that makes Mind-drain cut both ways: it railroads an
enemy *and* pacifies it, and a fully-drained wave walks the maze obediently past every turret.

Aggro is sticky without a timer or a flag: the enemy is walking toward the thing that pulled it,
so it stays inside the radius until the structure falls and only then does the core come back.
Nothing oscillates at the edge of the circle.

Below the radius, towers are still **handled by not handling them**. `AttackerComponent` scans
its own hitbox every physics frame and damages whatever structure is in it with no help from a
targeter, so a turret the enemy merely squeezes past gets chewed anyway — because the enemy is
standing next to it, not because anything chose it.

`Railroaded` on the targeter is derived from the route cursor (§8): true while the enemy is
walking wards or on the last leg home, false when no route is laid. Deviation will be the other
way off the route.

**Seeing it:** `EnemyTargetDebug` on the enemy draws the aggro ring and a line to the current
objective, coloured by state — aqua for the core, orange for a turret it broke off onto, purple for
the ward it is walking toward, grey for nothing reachable, and no ring at all while railroaded. It exists because the radius is a `float`
compared against `DistanceSquaredTo` rather than a collision shape, so Godot's own shape debug
cannot show it.

**Reusing the approach resolver is the point.** It was tempting to write a simpler targeter for
a single target that never moves — one `MapGetClosestPoint` and done. That is wrong, and visibly
so: with nothing carved out from under the core, the closest mesh point to its centre *is* its
centre, so every enemy walks into the core's collision and grinds against it forever. Standing a
target off from its **footprint** is exactly what `EnemyApproachResolver` already solves for
towers, and it is not cheap to re-derive. One class, one group setting.

The bake was the other half: it scanned the `Towers` group, so the core — deliberately not in it —
was never cut out of the mesh at all. It now scans `Structures`, which is both.

## 8. Walking the route

An enemy built by `CoreTargetingPattern` follows the route when one is laid. The targeter it
already has does the walking — one more mode beside "choose a structure", not a second node — and
emits each ward as a plain destination, so the controller never learns there is a route at all.

**Which ward am I on** belongs to `RouteCursor<T>` (`scripts/world/paths/core/`), engine-free and
covered by `RouteCursorTests`. It has three stages:

| Stage | Meaning | Railroaded |
|---|---|---|
| **Free** | no wards laid | no |
| **Following** | walking toward a ward | yes |
| **Home** | past the last ward, on the leg into the core | yes |

The rules, each answering one of the questions this section used to leave open:

- **Joining is at the far end**, wherever the enemy spawned. The last ward placed is the entrance;
  joining at the *nearest* ward instead would let an enemy that spawns beside the middle of a maze
  skip everything behind it, and the maze would be only as long as the nearest spawn allows.
- **Appending moves nobody.** The route is stored core-outward, so a ward added at the far end
  shifts no index, and enemies already walking carry on.
- **Lifting the ward an enemy is walking toward** sends it to the next surviving ward *inward*. The
  route rejoins over the gap (§5), so the enemy does too. The cursor tracks its target by
  **identity**, not index: lifting a ward behind an enemy shifts every index above it, and an index
  alone would silently point it at the wrong ward.
- **Lifting every ward** leaves no route, so the enemy is free — and turns on any turret in range.
- **Home ignores new wards.** An enemy on its last leg is not dragged back out to a far end laid
  after it passed.
- **An unreachable ward is skipped.** Placement keeps towers off the route (§4), so the only way a
  ward is unreachable is terrain — an island the enemy cannot get to. Walking at it forever would
  stall the wave, so it moves on inward.

A ward is reached inside one tile of its centre. Tighter, and a crowd jostling round one ward could
hold an enemy off it forever.

Known rough edges:

- An enemy hunting a turret when the first ward is laid drops the turret and walks to the far end.
  Defensible — the route is what it now believes — but it reads as abrupt.
- With no Mind roll there is no deviation, so every enemy on a laid route walks all of it.

## 9. Not done here

- **No lose condition.** The core raises `Fallen` and nothing listens.
- **Deviation**, which needs the Mind roll (`enemy-mind.md` §4) and a fourth cursor stage for an
  enemy that has stepped off the route and must not rejoin it.
- **More than one route.** One path, one spawn, one core. Branching is undesigned.
- **Persistence.** The route lives in the scene tree and dies with it; nothing saves it beside a
  `CrystalTemplate`.
- **Cost or limits**, per §3.
