using Godot;
using Godot.Collections;
using towerdefensegame.scripts.towers.crystal.ui;

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
    private CrystalLatticeEditor _latticeEditor;
    private TurretTower _editing;

    public override void _Ready()
    {
        BuildUI();
        Visible = false; // pocket starts as mini viewport; DimensionSwapped wired in scene

        if (PlacementManager != null)
            PlacementManager.TowerEditRequested += OnTowerEditRequested;
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

        var editButton = new Button { Text = "Edit Crystals" };
        editButton.Pressed += () => PlacementManager?.BeginEditing();
        vbox.AddChild(editButton);

        _cancelButton = new Button { Text = "Cancel" };
        _cancelButton.Pressed += () => PlacementManager?.Cancel();
        vbox.AddChild(_cancelButton);
    }

    // ── Crystal lattice editing ──────────────────────────────────────────────────

    /// <summary>
    /// Open the clicked tower's lattice full-screen, with the battle paused. Editing a lattice
    /// means reading a compile trace — doing that while a wave advances would make the tool a
    /// liability, and the tower would go on firing a stale shot besides.
    /// </summary>
    private void OnTowerEditRequested(Node2D tower)
    {
        if (tower is not TurretTower turret) return;
        if (turret.Lattice == null)
        {
            GD.Print("[lattice] that tower has no crystal lattice");
            return;
        }

        _editing = turret;

        if (_latticeEditor == null)
        {
            _latticeEditor = new CrystalLatticeEditor
            {
                // must keep running while the tree is paused, or the editor freezes with it
                ProcessMode = ProcessModeEnum.Always,
            };
            _latticeEditor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _latticeEditor.LatticeEdited += () => _editing?.Recompile();
            _latticeEditor.CloseRequested += CloseLatticeEditor;
            AddChild(_latticeEditor);   // added last, so it draws over the placement panel
        }

        _latticeEditor.Visible = true;
        _latticeEditor.Edit(turret.Lattice, turret.Name, turret.CoreEnergy);
        GetTree().Paused = true;
    }

    private void CloseLatticeEditor()
    {
        GetTree().Paused = false;
        if (_latticeEditor != null) _latticeEditor.Visible = false;

        // the lattice was edited in place, so the tower already holds the new shot
        _editing?.Recompile();
        _editing = null;
        PlacementManager?.Cancel();
    }

    // ── Signal handler ───────────────────────────────────────────────────────────

    private void OnDimensionSwapped(bool pocketIsMain)
    {
        Visible = pocketIsMain;
        if (!pocketIsMain && _latticeEditor is { Visible: true }) CloseLatticeEditor();
    }
}
