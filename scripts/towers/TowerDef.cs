using Godot;
using Godot.Collections;
using towerdefensegame.scripts.towers.crystal;

namespace towerdefensegame.scripts.towers;

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
    [Export] public Vector2I SizePixels { get; set; } = new(16, 16);

    /// <summary>Texture shown as a semi-transparent ghost during placement preview.</summary>
    [Export] public Texture2D PreviewTexture { get; set; }

    /// <summary>World-pixel radius of this tower's targeting zone.</summary>
    [Export] public float TargetRadius { get; set; }

    /// <summary>HP removed from a target per shot. Used as-is when the tower has no crystal
    /// lattice; with one, <see cref="DamagePerWeaponEnergy"/> takes over.</summary>
    [Export] public int Damage { get; set; } = 2;

    /// <summary>
    /// The crystal lattice this tower type starts with — its shape and default crystals
    /// (`crystal/CrystalTemplate.cs`). Leave null for a tower with no lattice, which keeps the
    /// flat <see cref="Damage"/> behaviour.
    /// </summary>
    [Export] public CrystalTemplate Lattice { get; set; }

    /// <summary>
    /// Energy the tower's core feeds into the lattice each shot. A **tower** stat, not a lattice
    /// one — the same template in a bigger tower simply gets more to spend, and the crystals'
    /// costs are what decide how much survives to the weapon.
    /// </summary>
    [Export] public double CoreEnergy { get; set; } = 600;

    /// <summary>
    /// HP per unit of weapon energy delivered. **Placeholder** — mapping a compiled shot to
    /// damage properly is the combat track's job (roadmap item 4), where each op does its own
    /// work. Until then this keeps a lattice visibly connected to the gun.
    /// </summary>
    [Export] public float DamagePerWeaponEnergy { get; set; } = 0.02f;

    /// <summary>Seconds between shots once aimed.</summary>
    [Export] public float FireInterval { get; set; } = 0.5f;

    /// <summary>Resources consumed when the tower is built.</summary>
    [Export] public Array<TowerCost> Cost { get; set; } = new();
}
