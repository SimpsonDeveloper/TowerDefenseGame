using Godot;

/// <summary>
/// A typed Sprite2D used as a sprite component. Other systems (e.g. InventoryPickup,
/// InventoryUI) reference this to access the sprite's texture without coupling to a
/// raw Sprite2D.
/// </summary>
public partial class SpriteComponent : Sprite2D
{
}
