# A* Grid — Setup Guide

**Scripts:**
- `scripts/world/enemies/AStarGridManager.cs` — shared grid node
- `scripts/world/enemies/EnemyAStarController.cs` — enemy that requests paths

Uses Godot's built-in `AStarGrid2D` on a tile grid derived from `ChunkManager`
terrain data. Produces optimal paths. Enemies cache their path and recompute
periodically, so the per-frame cost is just following waypoints.

---

## Scene setup

### 1. Add AStarGridManager to each world scene

```
OverworldViewport (SubViewport)
├─ TerrainGen
├─ ChunkManager
├─ AStarGridManager          ← add this node
├─ PlayerController
└─ ...
```

Wire the `ChunkManager` export on `AStarGridManager` in the Inspector.

Add `AStarGridManager` to the group **`astar_manager`** (Node panel → Groups tab).
`EnemyAStarController` finds it via this group.

### 2. Hook chunk loading into the grid

In `ChunkManager` (or wherever you apply completed chunk data), call:

```csharp
astarGridManager.MarkChunkDirty(chunkCoord);
```

This queues a per-chunk grid refresh on the next `_Process` frame so newly loaded
terrain is reflected in paths.

### 3. Enemy scene node tree

```
BossEnemy (CharacterBody2D)    ← script: BossEnemy.cs : EnemyAStarController
└─ CollisionShape2D
└─ AnimatedSprite2D  (optional)
```

No additional child nodes required.

---

## Subclass example

```csharp
public partial class BossEnemy : EnemyAStarController
{
    protected override void OnPhysicsTick(double delta, float distanceToTarget)
    {
        if (distanceToTarget <= StopDistance)
            SmashAttack();
    }
}
```

---

## Key exports (Inspector)

| Export | Default | Notes |
|---|---|---|
| `MoveSpeed` | 80 | px/s |
| `Acceleration` | 10 | |
| `WaypointReachedDistance` | 10 | px, advance to next waypoint |
| `StopDistance` | 20 | px |
| `PathUpdateInterval` | 0.4 | Seconds between full A* recomputes |
| `PathInvalidationDistance` | 64 | px target movement that forces early recompute |
| `TargetGroup` | "player" | |

**AStarGridManager exports:**

| Export | Default | Notes |
|---|---|---|
| `TileSize` | 16 | Must match TileMap tile size |
| `GridRadius` | 160 | Tiles from origin; total grid = 320×320 |

---

## Performance notes

- `AStarGrid2D` runs on the main thread. Path requests happen at most every
  `PathUpdateInterval` seconds per enemy, so cost scales with enemy count.
- The grid covers `(2 × GridRadius)²` tiles. At radius 160 that is 102 400 tiles —
  memory is small (booleans), but the initial `RefreshFullGrid()` iterates all of
  them. For very large worlds, increase radius lazily or only refresh loaded chunks.
- Diagonal movement is enabled only when both adjacent cardinal neighbours are clear,
  which prevents corner-cutting through diagonal gaps between tiles.

---

## Limitations

- Paths are computed in world-space and become stale if terrain changes under a
  cached path. `PathInvalidationDistance` handles a moving target but not moving
  terrain. If destructible terrain is added later, call `MarkChunkDirty` on
  destruction events as well.
