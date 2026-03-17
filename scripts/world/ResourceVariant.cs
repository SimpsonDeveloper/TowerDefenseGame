using Godot;
using towerdefensegame.scripts.world;

/// <summary>
/// Data resource describing one harvestable resource variant.
/// Create instances via Project → New Resource → ResourceVariant and assign textures.
/// </summary>
[GlobalClass]
public partial class ResourceVariant : Resource
{
    /// <summary>Sprite shown on the harvestable node in the world.</summary>
    [Export] public Texture2D HarvestableTexture { get; set; }

    /// <summary>Sprite shown on the drop after harvesting.</summary>
    [Export] public Texture2D DropTexture { get; set; }

    /// <summary>Name used in the inventory when this resource is picked up.</summary>
    [Export] public string ItemName { get; set; } = "Resource";
    
    /// <summary> </summary>
    [Export] public ResourceEnum ResourceEnum { get; set; } = ResourceEnum.CrystalBlue;
}
