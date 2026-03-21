# NavigationAgent2D — Setup Guide

**Scripts:** `scripts/world/enemies/EnemyNavAgentController.cs`

Delegates all pathfinding to Godot's built-in `NavigationAgent2D`. Produces optimal
paths through any obstacle shape, including player-built walls and corridor maps.
Requires a one-time navigation mesh bake per world.

---

## Scene setup

### 1. Add a NavigationRegion2D to each world

In the Overworld scene (`main_scene.tscn`) and Pocket Dimension scene, add:

```
NavigationRegion2D
└─ (NavigationPolygon resource assigned below)
```

### 2. Configure the NavigationPolygon

- Select the `NavigationRegion2D` node.
- In the Inspector, create a new `NavigationPolygon` resource.
- Under **Parsed Geometry** → **Source Geometry Mode**, set to
  `Static Colliders` (or `Groups with Children`).
- Set **Parsed Geometry** → **Collision Mask** to match the layer your terrain
  collision is on (layer 1 by default).
- Click **Bake NavigationPolygon** in the toolbar.

> For static terrain this bake only needs to happen once. For procedurally loaded
> chunks you will need to rebake or use `NavigationServer2D` incremental baking
> (see *Dynamic rebaking* below).

### 3. Enemy scene node tree

```
EnemyNavAgent (CharacterBody2D)       ← script: EnemyNavAgentController.cs (or subclass)
├─ CollisionShape2D
├─ NavigationAgent2D                  ← required child, exact name matters
└─ AnimatedSprite2D  (optional)
```

The script looks for `"NavigationAgent2D"` by name in `_Ready`.

---

## Subclass example

```csharp
public partial class ArcherSkeleton : EnemyNavAgentController
{
    protected override void OnReady()
    {
        // cache nodes
    }

    protected override void OnPhysicsTick(double delta, float distanceToTarget)
    {
        if (distanceToTarget <= TargetDesiredDistance)
            ShootArrow();
    }
}
```

---

## Key exports (Inspector)

| Export | Default | Notes |
|---|---|---|
| `MoveSpeed` | 80 | px/s |
| `Acceleration` | 10 | |
| `PathDesiredDistance` | 8 | px, waypoint advance threshold (≈ half tile) |
| `TargetDesiredDistance` | 20 | px, navigation "finished" threshold |
| `TargetUpdateInterval` | 0.15 | Seconds between TargetPosition pushes |
| `TargetGroup` | "player" | |

---

## Dynamic rebaking (chunked terrain)

Because the Overworld generates terrain dynamically, tiles may not exist when the
initial bake runs. Two options:

**Option A — Bake on chunk load (recommended for correctness):**

In `ChunkManager`, after applying a completed chunk, call:

```csharp
NavigationServer2D.BakeFromSourceGeometryData(navRegion.NavigationPolygon, sourceGeom);
```

This is async and will not stall the frame. See Godot docs for
`NavigationMeshSourceGeometryData2D`.

**Option B — Defer to context steering near unloaded terrain:**

Keep `EnemyController` as the default and switch to `EnemyNavAgentController` only
in bounded areas (e.g. the Pocket Dimension, which pre-generates all chunks). This
avoids the rebaking complexity entirely for the Overworld.

---

## Performance notes

- `NavigationServer2D` runs path queries on a background thread; the main thread
  only receives results via `GetNextPathPosition()`. Runtime cost per enemy is
  very low.
- Rebaking is the expensive operation. Batch chunk loads and debounce the rebake
  call (e.g. only rebake after 500ms of no new chunks) to avoid thrashing.
