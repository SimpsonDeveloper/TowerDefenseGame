using Godot;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Data resource describing one harvestable resource data.
/// Create instances via Project → New Resource → ResourceData and assign textures.
/// </summary>
[GlobalClass]
public partial class ResourceData : Resource
{
    /// <summary>Sprite shown on the harvestable node in the world.</summary>
    [Export] public Texture2D HarvestableTexture { get; set; }

    /// <summary>Sprite shown on the drop after harvesting.</summary>
    [Export] public Texture2D DropTexture { get; set; }
    
    /// <summary>Resource Id</summary>
    [Export] public ResourceId ResourceId { get; set; }
}
