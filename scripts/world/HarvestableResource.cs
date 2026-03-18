using Godot;

/// <summary>
/// Component that handles resource-specific behaviour for a harvestable node:
/// applies the variant textures in _Ready and spawns drops when the sibling
/// Harvestable emits its Broken signal (wired up in the scene).
/// </summary>
public partial class HarvestableResource : Node
{
    [Export] public ResourceVariant Variant { get; set; }
    [Export] public PackedScene DropScene { get; set; }
    [Export] public SpriteComponent Sprite { get; set; }
    [Export] public int MinDropCount { get; set; } = 1;
    [Export] public int MaxDropCount { get; set; } = 3;

    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();

        if (Variant?.HarvestableTexture != null && Sprite != null)
            Sprite.Texture = Variant.HarvestableTexture;
    }

    // Connected to Harvestable.Broken in the scene.
    private void OnHarvestableBroken()
    {
        SpawnDrops(GetParent<Node2D>().GlobalPosition);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SpawnDrops(Vector2 position)
    {
        if (DropScene == null)
            return;

        int count = _rng.RandiRange(MinDropCount, MaxDropCount);
        for (int i = 0; i < count; i++)
            TrySpawnDrop(position);
    }

    private void TrySpawnDrop(Vector2 position)
    {
        var drop = DropScene.Instantiate();

        if (drop is not Node2D dropNode)
        {
            GD.PushWarning($"HarvestableResource '{Name}': DropScene root must be a Node2D.");
            drop.QueueFree();
            return;
        }

        bool hasHarvestDrop = false;
        foreach (var child in dropNode.GetChildren())
        {
            if (child is HarvestDrop)
            {
                hasHarvestDrop = true;
                break;
            }
        }

        if (!hasHarvestDrop)
        {
            GD.PushWarning($"HarvestableResource '{Name}': DropScene has no HarvestDrop component as a direct child.");
            dropNode.QueueFree();
            return;
        }

        if (Variant?.DropTexture != null)
        {
            var dropSprite = dropNode.GetNodeOrNull<SpriteComponent>("DropSprite");
            if (dropSprite != null)
                dropSprite.Texture = Variant.DropTexture;
        }

        if (Variant?.ItemName != null)
        {
            var pickup = dropNode.GetNodeOrNull<InventoryPickup>("InventoryPickup");
            if (pickup != null)
                pickup.ItemName = Variant.ItemName;
        }

        GetParent<Node2D>().GetParent().AddChild(dropNode);
        dropNode.GlobalPosition = position;
    }
}
