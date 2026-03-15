using Godot;

/// <summary>
/// Picks up the parent drop Node2D when the player enters the HitBox.
/// Also, despawns the drop after DespawnTime seconds if never collected.
/// Movement and magnetize behavior live in DropPhysics.
/// </summary>
public partial class InventoryPickup : Node
{

    [Export] public DetectionZone HitBox { get; set; }

    [Export] public SpriteComponent Sprite { get; set; }

    /// <summary>Seconds before an uncollected drop despawns. Default is 5 minutes.</summary>
    [Export] public float DespawnTime { get; set; } = 300f;

    public string ItemName { get; set; } = "Unknown";
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
