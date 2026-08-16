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
    ///
    /// The editor itself is <b>not</b> a child of this node, and cannot be. This UI lives inside
    /// the pocket dimension's <c>SubViewport</c>, which receives input only because the
    /// <c>SubViewportContainer</c> above it forwards it — and a paused container forwards nothing.
    /// A modal hosted in here would go deaf the instant it paused the game, whatever
    /// <c>ProcessMode</c> it set on itself: <c>Always</c> governs whether an event is handled, not
    /// whether one arrives. So it hangs off the scene's <c>WhenPaused</c> branch instead, found by
    /// group (<see cref="CrystalLatticeEditor.GroupName"/>), and this class only drives it.
    /// </summary>
    private void OnTowerEditRequested(Node2D tower)
    {
        if (Editor() == null)
        {
            GD.PushWarning(
                $"[lattice] no node in group '{CrystalLatticeEditor.GroupName}' — " +
                "the scene needs a CrystalLatticeEditor under WhenPaused");
            return;
        }

        if (tower is not TurretTower turret) return;
        if (turret.Lattice == null)
        {
            GD.Print("[lattice] that tower has no crystal lattice");
            return;
        }

        _editing = turret;

        _latticeEditor.Visible = true;
        _latticeEditor.Edit(turret.Lattice, turret.DisplayName, turret.CoreEnergy);
        GetTree().Paused = true;
    }

    /// <summary>
    /// Finds the editor and subscribes, once, on first use. Deliberately not in
    /// <c>_Ready</c>: the group is filled by the editor's own <c>_Ready</c>, and which of the two
    /// runs first is decided by the order <c>WhenPaused</c> and <c>Pausable</c> happen to sit in
    /// the scene. Nothing should break from moving a branch up or down.
    /// </summary>
    private CrystalLatticeEditor Editor()
    {
        if (_latticeEditor != null) return _latticeEditor;

        _latticeEditor = GetTree().GetFirstNodeInGroup(CrystalLatticeEditor.GroupName)
            as CrystalLatticeEditor;
        if (_latticeEditor == null) return null;

        _latticeEditor.LatticeEdited += () => _editing?.Recompile();
        _latticeEditor.CloseRequested += CloseLatticeEditor;
        return _latticeEditor;
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
        // the editor is not below this node, so it does not hide with it — close it explicitly
        if (!pocketIsMain && _latticeEditor is { Visible: true }) CloseLatticeEditor();
    }
}
