using Godot;

/// <summary>
/// CanvasLayer that renders the player's inventory as a row of slots anchored to the
/// bottom-center of the screen. Each slot shows the item sprite and stack count.
/// Updates automatically whenever Inventory.InventoryChanged is emitted.
/// </summary>
public partial class InventoryUI : CanvasLayer
{
    [Export] public Inventory Inventory { get; set; }

    private HBoxContainer _slotsContainer;

    private const int SlotSize    = 48;
    private const int SlotPadding = 4;

    public override void _Ready()
    {
        Layer = 20;

        // Full-screen anchor so child layout can reference screen edges.
        // MouseFilter.Ignore on all layout containers so they don't swallow input
        // intended for UI on layers below (e.g. terrain sliders).
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        // VBoxContainer fills the screen; a spacer pushes slots to the bottom
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(vbox);

        var spacer = new Control();
        spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        spacer.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(spacer);

        _slotsContainer = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _slotsContainer.AddThemeConstantOverride("separation", SlotPadding);
        vbox.AddChild(_slotsContainer);

        // Bottom margin so slots don't sit flush with the screen edge
        var bottomPad = new Control
        {
            CustomMinimumSize = new Vector2(0, SlotPadding),
            MouseFilter       = Control.MouseFilterEnum.Ignore,
        };
        vbox.AddChild(bottomPad);

        if (Inventory != null)
            Inventory.InventoryChanged += Refresh;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Refresh()
    {
        foreach (Node child in _slotsContainer.GetChildren())
            child.QueueFree();

        foreach (var item in Inventory.Items)
            _slotsContainer.AddChild(CreateSlot(item));
    }

    private static Control CreateSlot(Inventory.InventoryItem item)
    {
        var panel = new Panel
        {
            CustomMinimumSize = new Vector2(SlotSize, SlotSize),
        };

        if (item.Sprite != null)
        {
            var texture = new TextureRect
            {
                Texture     = item.Sprite,
                ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            panel.AddChild(texture);
        }

        var label = new Label
        {
            Text           = item.Count.ToString(),
            AnchorLeft     = 1f,
            AnchorRight    = 1f,
            AnchorBottom   = 1f,
            AnchorTop      = 1f,
            GrowHorizontal = Control.GrowDirection.Begin,
            GrowVertical   = Control.GrowDirection.Begin,
        };
        panel.AddChild(label);

        return panel;
    }
}
