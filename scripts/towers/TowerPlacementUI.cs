using Godot;
using Godot.Collections;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Mock placement UI: a panel anchored to the right edge of the pocket dimension
/// viewport with one button per available tower type and a Cancel button.
///
/// Visibility is tied to the pocket dimension being the main viewport via the
/// WorldManager.DimensionSwapped signal.
/// </summary>
public partial class TowerPlacementUI : CanvasLayer
{
    [Export] public TowerPlacementManager PlacementManager { get; set; }
    [Export] public Array<TowerDef> AvailableTowers { get; set; } = new();

    private Button _cancelButton;

    public override void _Ready()
    {
        BuildUI();
        Visible = false; // pocket starts as mini viewport; DimensionSwapped wired in scene
    }

    // ── UI construction ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var panel = new PanelContainer();
        // Anchor to the full right edge of the viewport.
        panel.AnchorLeft     = 1f;
        panel.AnchorRight    = 1f;
        panel.AnchorTop      = 0f;
        panel.AnchorBottom   = 1f;
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        AddChild(panel);

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        var header = new Label { Text = "Place Tower" };
        vbox.AddChild(header);

        foreach (var def in AvailableTowers)
        {
            if (def == null) continue;
            var captured = def; // avoid closure capture of loop variable
            var btn = new Button { Text = captured.DisplayName };
            btn.Pressed += () => PlacementManager?.BeginPlacement(captured);
            vbox.AddChild(btn);
        }

        var destroyButton = new Button { Text = "Destroy Mode" };
        destroyButton.Pressed += () => PlacementManager?.BeginDestroying();
        vbox.AddChild(destroyButton);

        _cancelButton = new Button { Text = "Cancel" };
        _cancelButton.Pressed += () => PlacementManager?.Cancel();
        vbox.AddChild(_cancelButton);
    }

    // ── Signal handler ───────────────────────────────────────────────────────────

    private void OnDimensionSwapped(bool pocketIsMain) => Visible = pocketIsMain;
}
