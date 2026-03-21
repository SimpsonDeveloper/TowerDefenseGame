# Flow Field — Setup Guide

**Scripts:**
- `scripts/world/enemies/FlowFieldManager.cs` — computes the field via BFS
- `scripts/world/enemies/EnemyFlowFieldController.cs` — enemy that samples it

A BFS propagates outward from the target tile. Every reachable tile stores a
direction vector pointing one step closer to the target. Enemies read their tile's
vector each frame — a single dictionary lookup regardless of swarm size.

Best for wave-based tower defense scenarios where 100–500 enemies all converge on
the same goal (e.g. a base or the player).

---

## Scene setup

### 1. Add FlowFieldManager to each world scene

```
OverworldViewport (SubViewport)
├─ ChunkManager
├─ FlowFieldManager          ← add this node
├─ PlayerController
└─ ...
```

Wire the `ChunkManager` export in the Inspector.
`FlowFieldManager` adds itself to the group **`flow_field_manager`** in `_Ready`.

### 2. Assign the target at runtime

The `FlowFieldManager` needs to know what to point toward. Set the target once
when the player spawns (or in your wave manager):

```csharp
// In a wave manager or world setup script:
var ff = GetTree().GetFirstNodeInGroup("flow_field_manager") as FlowFieldManager;
var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
ff.SetTarget(player);
```

The field auto-recomputes whenever the target moves more than `RecomputeThreshold`
pixels (default: one tile, 16px). The BFS is synchronous but fast — at radius 128
it visits at most ~52 000 tiles, which runs in under 1 ms on modern hardware for
mostly-open terrain.

### 3. Enemy scene node tree

```
SwarmEnemy (CharacterBody2D)   ← script: SwarmEnemy.cs : EnemyFlowFieldController
└─ CollisionShape2D
```

---

## Subclass example

```csharp
public partial class SwarmEnemy : EnemyFlowFieldController
{
    protected override void OnPhysicsTick(double delta, float distanceToTarget)
    {
        if (distanceToTarget <= StopDistance)
            DealDamage();
    }
}
```

---

## Key exports (Inspector)

| Export | Default | Notes |
|---|---|---|
| `MoveSpeed` | 80 | px/s |
| `Acceleration` | 8 | |
| `StopDistance` | 20 | px |
| `TargetGroup` | "player" | Used for direct-seek fallback only |

**FlowFieldManager exports:**

| Export | Default | Notes |
|---|---|---|
| `TileSize` | 16 | Must match TileMap tile size |
| `FieldRadius` | 128 | Tiles from target; enemies outside fall back to direct seek |
| `RecomputeThreshold` | 16 | px of target movement before recomputing |

---

## Performance notes

- **Per-enemy cost:** one `Dictionary<Vector2I, Vector2>` lookup per physics frame.
  500 enemies doing a dictionary lookup per frame is negligible.
- **Recompute cost:** BFS over walkable tiles within `FieldRadius`. Runs on the
  main thread. At radius 128 and mostly-open terrain, expect < 1ms. If the target
  moves frequently (every frame), raise `RecomputeThreshold` to reduce frequency.
- If you add multiple targets (e.g. several towers), create one `FlowFieldManager`
  per target and assign enemies to the nearest one. Fields are independent and
  can coexist.

---

## Limitations

- Only one target per field instance. For multi-target scenarios, add multiple
  `FlowFieldManager` nodes and partition enemies between them.
- The BFS is cardinal-only (4 directions). Enemies move smoothly because velocity
  is interpolated, but paths are slightly longer than diagonal-enabled variants.
- Large `FieldRadius` values with dense blocked terrain increase BFS time. Profile
  if you notice spikes when the target moves into a complex area.
