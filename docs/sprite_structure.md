# Sprite Structure

How sprites are sized and rendered in this project.

## Pipeline

- Render target: **1920×1080**, `stretch/mode = canvas_items`
- World unit = framebuffer pixel (at `Camera.Zoom = 1`)
- Tile size: **`TilePixelSize = 16`** world units
- Default texture filter: **Nearest** (set globally and on the OverworldViewport)
- Camera zoom is locked at 1.

## The rule

> Every pixel-art `Sprite2D` gets **`scale = Vector2(2, 2)`**.
> Filter stays at the Nearest default (no per-sprite override needed).

Source PNGs are NOT re-imported — they stay at their authored size. The scale
puts them at "1 art-pixel = 2 screen-pixels," giving a uniform pixel grid
across the game.

## Hierarchy implications

- **Children of a scaled sprite inherit the scale.** Local `position`
  offsets stay in source-pixel units. A muzzle marker at `(30, 0)` under a
  scaled `TurretSprite` renders 60 world units away — correct.
- **Root-level world positions are in world units.** A spawn origin at
  `(1152, 648)` means 1152 world pixels = 72 tiles.
- **Collision shapes attached to the body (not the sprite) are in world units.**
  A `CircleShape2D.radius = 28` on the player root body means a 28-world-unit
  radius regardless of any sibling sprite's scale.

## Coordinate conversions inside scaled subtrees

When code computes a world-space distance (e.g. `GlobalPosition.DistanceTo(...)`)
and assigns it to something living inside a scaled parent (Line2D points,
particle positions, child offsets), **divide by the parent's `Scale` to bring
it into local space first.** Otherwise the assignment renders at `Scale ×`
the intended size.

Concrete example, `TurretTower.cs`:

```csharp
float parentScale = TurretSprite.Scale.X;            // 2
float beamLength = distanceToTarget / parentScale    // world → local
                   - _muzzleOffset;                  // already local
LaserLine.SetPointPosition(1, new Vector2(beamLength, 0));
```

## Rotating sprites

Same treatment as static sprites: `scale = 2`, Nearest filter, source texture
left as-is. Rotation is rasterized at full 1920×1080, which already halves
jagged-step thickness compared to the previous 960×540 framebuffer.

If a specific rotating sprite still looks too chunky in motion, **re-author
that texture at higher source resolution** (e.g. 128×128 instead of 64×64,
keeping the same on-screen footprint). Do not switch its filter to Linear —
that breaks the uniform pixel-grid look.

## Tower placement preview

`TowerPlacementManager` builds a ghost `Sprite2D` from `TowerDef.PreviewTexture`
at runtime. It explicitly sets `Scale = (2, 2)` to match the in-scene tower
sprite convention. Any future code that constructs a Sprite2D at runtime must
do the same.

## SubViewport-backed sprites

The pattern from `player_controller.tscn`:

- A `SubViewport` (e.g. 32×32) renders particles or other visuals into a
  texture.
- A `SubViewportSprite` (`Sprite2D` with `ViewportTexture`) displays the
  result in the world.
- **The SubViewportSprite gets `scale = 2`**, same as any other pixel sprite.
- Positions and emission shapes *inside* the SubViewport are in its own
  coordinate space and do NOT scale with the world migration.

## Particle systems & other VFX — TODO

Pixellization strategy for `GPUParticles2D` (and shader-driven effects like
lasers, portals, crystal cracks) is not yet defined. The existing SubViewport
pattern from `player_controller.tscn` is one option (render particles to a
small framebuffer, display via a scaled Sprite2D). Open questions:

- Which effects need to read as pixel art vs. which can stay smooth?
- Per-effect SubViewport vs. a shared low-res particle layer?
- Performance budget for many SubViewports at once?

Fill this in once the approach is chosen.
