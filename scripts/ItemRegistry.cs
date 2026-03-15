using System.Collections.Generic;
using Godot;

/// <summary>
/// Autoload singleton. Scans res://assets/world_resources/variants/ at startup and
/// registers every ResourceVariant .tres found there. Add to autoloads via
/// Project → Project Settings → Autoload.
/// </summary>
public partial class ItemRegistry : Node
{
    public static ItemRegistry Instance { get; private set; }

    private const string VariantsPath = "res://assets/world_resources/variants";

    private readonly Dictionary<string, ResourceVariant> _variants = new();

    public override void _Ready()
    {
        Instance = this;
        LoadVariants();
    }

    public ResourceVariant Get(string itemName) =>
        _variants.GetValueOrDefault(itemName);

    private void LoadVariants()
    {
        using var dir = DirAccess.Open(VariantsPath);
        if (dir == null)
        {
            GD.PushWarning($"ItemRegistry: could not open '{VariantsPath}'.");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != string.Empty)
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
            {
                var variant = GD.Load<ResourceVariant>($"{VariantsPath}/{fileName}");
                if (variant != null)
                    _variants[variant.ItemName] = variant;
                else
                    GD.PushWarning($"ItemRegistry: '{fileName}' could not be loaded as a ResourceVariant.");
            }
            fileName = dir.GetNext();
        }

        dir.ListDirEnd();
    }
}
