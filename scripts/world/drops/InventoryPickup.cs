using Godot;

/// <summary>
/// Component that allows its parent drop Node2D to be picked up by the player when
/// the player enters the HitBox. Requires a HitBoxComponent for proximity detection
/// and a SpriteComponent so the InventoryUI can render the item's sprite.
/// When picked up, the entire parent node is freed.
/// </summary>
public partial class InventoryPickup : Node
{
    [Export]
    public string ItemName { get; set; } = "Unknown";

    [Export]
    public DetectionZone HitBox { get; set; }

    [Export]
    public SpriteComponent Sprite { get; set; }

    /// <summary>Seconds before an unpicked-up drop despawns. Default is 5 minutes.</summary>
    [Export]
    public float DespawnTime { get; set; } = 300f;

    private bool  _pickedUp;
    private float _age;

    public override void _Ready()
    {
        if (HitBox == null)
            GD.PushWarning($"InventoryPickup on '{GetParent()?.Name}': HitBox is not set.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_pickedUp || HitBox == null)
            return;

        _age += (float)delta;
        if (_age >= DespawnTime)
        {
            GetParent().QueueFree();
            return;
        }

        foreach (var body in HitBox.GetOverlappingBodies())
        {
            if (body is not PlayerController player)
                continue;

            var inventory = player.GetNodeOrNull<Inventory>("Inventory");
            if (inventory == null)
            {
                GD.PushWarning("InventoryPickup: Player has no Inventory child node.");
                return;
            }

            _pickedUp = true;
            inventory.AddItem(ItemName, Sprite?.Texture);
            GetParent().QueueFree();
            return;
        }
    }
}
