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
    private CanvasLayer _editorLayer;
    private CrystalLatticeEditor _latticeEditor;
    private TurretTower _editing;

    /// <summary>Above every other overlay in the scene — those sit on the default layer 1.</summary>
    private const int EditorCanvasLayer = 100;

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
        _editorLayer ??= BuildEditorLayer();

        // the Control, not the layer: CrystalLatticeEditor gates its own escape handling on Visible
        _latticeEditor.Visible = true;
        _latticeEditor.Edit(turret.Lattice, turret.DisplayName, turret.CoreEnergy);
        GetTree().Paused = true;
    }

    /// <summary>
    /// Builds the editor into its own <see cref="CanvasLayer"/> under the <b>root</b> viewport,
    /// deliberately not under this node.
    ///
    /// This UI lives inside the pocket dimension's <c>SubViewport</c>, and a SubViewport only
    /// receives input because the <c>SubViewportContainer</c> above it forwards it — which that
    /// container can only do while it is processing input. Pausing the tree stops it, so anything
    /// hosted in there goes deaf the instant it pauses the game, however it sets its own
    /// <c>ProcessMode</c>: the events never arrive to be handled. A modal that outlives the pause
    /// has to hang off the viewport that reads the mouse directly.
    ///
    /// Being at the root also makes "full-screen" true — inside the SubViewport the editor was
    /// still underneath the other dimension's mini-view and the wave timer.
    /// </summary>
    private CanvasLayer BuildEditorLayer()
    {
        _latticeEditor = new CrystalLatticeEditor();
        _latticeEditor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _latticeEditor.LatticeEdited += () => _editing?.Recompile();
        _latticeEditor.CloseRequested += CloseLatticeEditor;

        CanvasLayer layer = new CanvasLayer
        {
            Layer = EditorCanvasLayer,
            // Always propagates to children, so the whole editor keeps running while paused.
            ProcessMode = ProcessModeEnum.Always,
        };
        layer.AddChild(_latticeEditor);

        // Parented to the root, so it must be freed by hand — see _ExitTree.
        GetTree().Root.AddChild(layer);
        return layer;
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

    public override void _ExitTree()
    {
        if (_editorLayer == null) return;

        // The layer hangs off the root, so unloading this scene would otherwise strand it —
        // and strand the pause with it.
        SceneTree tree = GetTree();
        if (tree != null && _latticeEditor is { Visible: true }) tree.Paused = false;

        _editorLayer.QueueFree();
        _editorLayer = null;
        _latticeEditor = null;
    }

    // ── Signal handler ───────────────────────────────────────────────────────────

    private void OnDimensionSwapped(bool pocketIsMain)
    {
        Visible = pocketIsMain;
        // the editor no longer hides with this node, so it has to be closed explicitly
        if (!pocketIsMain && _latticeEditor is { Visible: true }) CloseLatticeEditor();
    }
}
