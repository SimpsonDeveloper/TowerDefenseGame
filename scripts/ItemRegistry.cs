using System.Collections.Generic;
using Godot;

/// <summary>
/// Autoload singleton. Populate the Variants array in the editor with all
/// ResourceVariant .tres files. Add to autoloads via Project → Project Settings → Globals.
/// </summary>
public partial class ItemRegistry : Node
{
    public static ItemRegistry Instance { get; private set; }

    [Export] public ResourceVariant[] Variants { get; set; } = [];

    private readonly Dictionary<string, ResourceVariant> _variants = new();

    public override void _Ready()
    {
        Instance = this;

        foreach (var variant in Variants)
        {
            if (variant != null)
                _variants[variant.ItemName] = variant;
        }
    }

    public ResourceVariant Get(string itemName) =>
        _variants.GetValueOrDefault(itemName);
}
