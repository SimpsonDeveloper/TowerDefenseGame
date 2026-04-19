using Godot;
using Godot.Collections;

namespace towerdefensegame;

/// <summary>
/// Data resource describing one tower type.
/// SizePixels drives snap alignment: each axis independently uses
/// TowerSnapHelper.SnapAxis to decide center-of-tile vs tile-boundary alignment.
/// </summary>
[GlobalClass]
public partial class TowerDef : Resource
{
    /// <summary>Human-readable name shown in placement UI.</summary>
    [Export] public string DisplayName { get; set; } = "Tower";

    /// <summary>Scene to instantiate when placing or previewing this tower.</summary>
    [Export] public PackedScene TowerScene { get; set; }

    /// <summary>
    /// Pixel footprint of the tower sprite. Each axis must be a non-zero
    /// multiple of CoordConfig.TilePixelSize.
    /// </summary>
    [Export] public Vector2I SizePixels { get; set; } = new(8, 8);

    /// <summary>Texture shown as a semi-transparent ghost during placement preview.</summary>
    [Export] public Texture2D PreviewTexture { get; set; }

    /// <summary>World-pixel radius of this tower's targeting zone.</summary>
    [Export] public float TargetRadius { get; set; }

    /// <summary>Resources consumed when the tower is built.</summary>
    [Export] public Array<TowerCost> Cost { get; set; } = new();
}
