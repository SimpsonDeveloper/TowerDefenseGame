using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts;
/// <summary>
/// Autoload singleton. Populate the Variants array in item_registry.tcsn in the editor with all
/// ResourceData .tres files. Add item_registry.tcsn to autoloads via Project → Project Settings → Globals.
/// </summary>
public partial class ItemRegistry : Node2D
{
    public static ItemRegistry Instance { get; private set; }

    [Export] public ResourceData[] Resources { get; set; } = [];

    private readonly Dictionary<ResourceId, ResourceData> _resourcesById = new();

    public override void _Ready()
    {
        Instance = this;

        foreach (var resource in Resources)
        {
            if (resource != null)
                _resourcesById[resource.ResourceId] = resource;
        }
    }

    public ResourceData Get(ResourceId resourceId) =>
        _resourcesById.GetValueOrDefault(resourceId);
}
