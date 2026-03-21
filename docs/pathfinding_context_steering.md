# Context Steering — Setup Guide

**Script:** `scripts/world/enemies/EnemyController.cs`

Casts N rays in a radial pattern each tick. Each direction is scored for interest
(points toward target) minus danger (hits an obstacle). The highest-scoring clear
direction wins. No map data structures required.

---

## Minimum setup (2 steps)

1. Add `PlayerController` to the `"player"` group.
2. Create a `CharacterBody2D` scene, attach a subclass of `EnemyController` as its
   script, add a `CollisionShape2D` child.

That's it. No other nodes needed.

---

## Scene node tree

```
SlimeEnemy (CharacterBody2D)          ← script: SlimeEnemy.cs : EnemyController
└─ CollisionShape2D
└─ AnimatedSprite2D  (optional)
```

---

## Subclass example

```csharp
public partial class SlimeEnemy : EnemyController
{
    [Export] public int Damage { get; set; } = 5;
    private AnimatedSprite2D _sprite;

    protected override void OnReady()
    {
        _sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
    }

    protected override void OnPhysicsTick(double delta, float distanceToTarget)
    {
        _sprite.FlipH = Velocity.X < 0;

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
| `Acceleration` | 8 | Higher = snappier velocity changes |
| `StopDistance` | 20 | px, enemy stops closing in |
| `RayCount` | 8 | Reduce to 4 for cheaper enemies |
| `RayLength` | 48 | px, obstacle detection range |
| `UpdateInterval` | 0.12 | Seconds between steering recalcs |
| `TargetGroup` | "player" | Group name to search for target |
| `TargetPath` | *(empty)* | Explicit NodePath override |

---

## Performance notes

- Enemies stagger their first update automatically so a group spawning together
  does not produce a frame spike.
- The buffers (`_interest`, `_danger`, `_rayDirs`) are allocated once in `_Ready`
  and reused every tick — no per-frame heap allocation.
- `RayCount × (1 / UpdateInterval)` ≈ physics queries per second per enemy.
  At defaults: 8 rays × ~8 updates/s ≈ 64 queries/s/enemy. For 50 enemies that
  is ~3200/s, well within Godot's budget.

---

## Limitations

- Can get stuck in concave obstacles (e.g. a U-shaped rock formation). The fallback
  pushes directly toward the target, which lets `MoveAndSlide` slide along the wall,
  but the enemy may circle the trap for a second. If this is common in your level
  geometry, switch to `EnemyAStarController` for that enemy type.
