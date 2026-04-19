using Godot;

namespace towerdefensegame.scripts.world;

[GlobalClass]
public partial class ResourceId : Resource
{
    public static readonly ResourceId Unknown = new();
    [Export] public string Id;

    private ResourceId()
    {
        Id = "Unknown";
    }
    
    public ResourceId(string id)
    {
        Id = id; 
    }
}