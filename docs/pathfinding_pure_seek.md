# Pure Seek — Setup Guide

**Script:** `scripts/world/enemies/EnemySeekController.cs`

Moves in a straight line toward the target every frame. No obstacle avoidance.
`MoveAndSlide` handles collision response — the enemy slides along walls rather
than passing through them, but it will not navigate around them intentionally.

---

## When to use this

- **Flying or ghost enemies** that conceptually ignore terrain (bats, spectres, drones).
- **Projectile-like enemies** that move fast and rarely touch terrain.
- **Prototyping** — simplest script to get an enemy chasing the player immediately.

If the enemy is a ground unit that needs to navigate around rocks or water,
use `EnemyController` (context steering) instead.

---

## Minimum setup (2 steps)

1. Add `PlayerController` to the `"player"` group.
2. Create a `CharacterBody2D` scene with a `CollisionShape2D` and attach a subclass
   of `EnemySeekController`.

---

## Scene node tree

```
GhostEnemy (CharacterBody2D)   ← script: GhostEnemy.cs : EnemySeekController
└─ CollisionShape2D
└─ Sprite2D  (optional)
```

---

## Subclass example

```csharp
public partial class GhostEnemy : EnemySeekController
{
    protected override void OnPhysicsTick(double delta, float distanceToTarget)
    {
        if (distanceToTarget <= StopDistance)
            Haunt();
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
| `TargetGroup` | "player" | |
| `TargetPath` | *(empty)* | Explicit override |

---

## Making the enemy ignore terrain collision

For a true ghost effect, set the `CollisionMask` on the `CharacterBody2D` to `0`
(no layers). The enemy will pass through all physics bodies. Alternatively, create
a dedicated physics layer for "ghost-passable terrain" and exclude it from the mask.
