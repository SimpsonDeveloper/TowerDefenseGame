# Enemy Pathfinding — Overview

Five variants are implemented, each in `scripts/world/enemies/`. All share the same
public API surface (`SetTarget`, `ClearTarget`, `Target`, `OnReady`, `OnPhysicsTick`)
and register themselves in the `"enemies"` group on `_Ready`.

---

## Variant Summary

| File | Best for | Cost per enemy | Scene setup |
|---|---|---|---|
| `EnemyController` | General use, scattered obstacles | Very low | None |
| `EnemyNavAgentController` | Complex layouts, corridors | Very low (runtime) | NavigationRegion2D + bake |
| `EnemyAStarController` | Bosses, precision path | Low (cached) | AStarGridManager node |
| `EnemyFlowFieldController` | Massive swarms (100–500+) | Near-zero | FlowFieldManager node |
| `EnemySeekController` | Flying/ghost enemies | Negligible | None |

---

## Which to pick

```
Are enemies flying / phasing through walls?
  └─ Yes → EnemySeekController

Do you have 100+ enemies chasing the same target?
  └─ Yes → EnemyFlowFieldController

Do you need guaranteed optimal paths (boss, puzzle)?
  └─ Yes → EnemyAStarController

Does the map have corridors, doorways, or player-built walls?
  └─ Yes → EnemyNavAgentController (long-term best choice)

Otherwise (most ground enemies, scattered rocks/water):
  └─ EnemyController (context steering, recommended default)
```

---

## Common setup for all variants

1. **Add the player to the `"player"` group** in the Godot editor
   (select PlayerController → Node panel → Groups tab → type `player` → Add).

2. **Extend the controller** instead of using it directly:

   ```csharp
   public partial class SlimeEnemy : EnemyController   // or whichever variant
   {
       protected override void OnReady() { /* cache nodes */ }
       protected override void OnPhysicsTick(double delta, float dist)
       {
           if (dist <= StopDistance) Attack();
       }
   }
   ```

3. Set the script on a `CharacterBody2D` scene that has a `CollisionShape2D`.
   The `CollisionMask` on the node controls which physics layers block steering rays
   (for variants that use raycasts). Terrain collision is on layer 1 by default.

4. **Dual-world note:** each variant works in both the Overworld and Pocket Dimension
   because `GetWorld2D()` scopes physics queries to the `SubViewport` the enemy
   lives in. `GetNodesInGroup("player")` is scene-tree global — if the pocket
   dimension gains its own target later, assign it via `SetTarget()` instead of
   relying on the group lookup.

---

## Per-variant setup docs

- [Context Steering](pathfinding_context_steering.md)
- [NavigationAgent2D](pathfinding_nav_agent.md)
- [A\* Grid](pathfinding_astar.md)
- [Flow Field](pathfinding_flow_field.md)
- [Pure Seek](pathfinding_pure_seek.md)
