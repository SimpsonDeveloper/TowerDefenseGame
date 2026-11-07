using System.Collections.Generic;
using Godot;
using Godot.Collections;
using towerdefensegame.scripts.towers.crystal.ui;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Build-mode keys and the hint that shows them. Number keys pick what the mouse does:
///
///   1  Pan      — left-drag pans
///   2  Turrets  — left-click places, right-click destroys
///   3  Edit     — left-click a turret to open its crystals
///   4  Wards    — left-click lays, right-click lifts
///
/// Escape is back to Pan. Pressing 2 while already in turret mode steps to the next tower in
/// <see cref="AvailableTowers"/>. The modes themselves live on <see cref="TowerPlacementManager"/>;
/// this node maps keys to them and keeps the hint in step through its <c>ModeChanged</c> event.
///
/// The hint is plain labels, the active row marked with a caret — no colour or font set from
/// code, so the look stays in the theme.
///
/// Visibility is tied to the pocket dimension being the main viewport via the
/// WorldManager.DimensionSwapped signal, and keys are ignored while hidden.
/// </summary>
public partial class TowerPlacementUI : CanvasLayer
{
    [Export] public TowerPlacementManager PlacementManager { get; set; }
    [Export] public Array<TowerDef> AvailableTowers { get; set; } = new();

    /// <summary>
    /// The lattice editor, over in the scene's <c>WhenPaused</c> branch. See
    /// <see cref="OnTowerEditRequested"/> for why it has to live out there rather than under here.
    /// </summary>
    [Export] public CrystalLatticeEditor LatticeEditor { get; set; }

    private TurretTower _editing;

    /// <summary>One row per mode, in key order.</summary>
    private readonly Label[] _rows = new Label[4];

    /// <summary>Which of <see cref="AvailableTowers"/> key 2 places.</summary>
    private int _towerIndex;

    public override void _Ready()
    {
        BuildUI();
        Visible = false; // pocket starts as mini viewport; DimensionSwapped wired in scene

        if (LatticeEditor != null)
        {
            LatticeEditor.LatticeEdited += () => _editing?.Recompile();
            LatticeEditor.CloseRequested += CloseLatticeEditor;
        }

        if (PlacementManager != null)
        {
            PlacementManager.TowerEditRequested += OnTowerEditRequested;
            PlacementManager.ModeChanged += RefreshHint;
        }

        RefreshHint();
    }

    public override void _ExitTree()
    {
        if (PlacementManager != null)
        {
            PlacementManager.TowerEditRequested -= OnTowerEditRequested;
            PlacementManager.ModeChanged -= RefreshHint;
        }
    }

    // ── Keys ─────────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || PlacementManager == null) return;

        if (@event.IsActionPressed("mode_pan") || @event.IsActionPressed("ui_cancel"))
            PlacementManager.Cancel();
        else if (@event.IsActionPressed("mode_turrets"))
            SelectTurrets();
        else if (@event.IsActionPressed("mode_edit"))
            PlacementManager.BeginEditing();
        else if (@event.IsActionPressed("mode_wards"))
            PlacementManager.BeginWarding();
        else
            return;

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Turret mode; again while in it, the next tower.</summary>
    private void SelectTurrets()
    {
        List<TowerDef> towers = Towers();
        if (towers.Count == 0) return;

        if (PlacementManager.Mode == TowerPlacementManager.BuildMode.Turrets)
            _towerIndex = (_towerIndex + 1) % towers.Count;

        _towerIndex = Mathf.Clamp(_towerIndex, 0, towers.Count - 1);
        PlacementManager.BeginPlacement(towers[_towerIndex]);
    }

    private List<TowerDef> Towers()
    {
        List<TowerDef> towers = new();
        foreach (TowerDef def in AvailableTowers)
            if (def != null) towers.Add(def);
        return towers;
    }

    // ── Mode hint ────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Top-right, sized to its text. Layout only; the look is the theme's.
        PanelContainer panel = new()
        {
            AnchorLeft     = 1f,
            AnchorRight    = 1f,
            GrowHorizontal = Control.GrowDirection.Begin,
            MouseFilter    = Control.MouseFilterEnum.Ignore,
        };
        AddChild(panel);

        VBoxContainer rows = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(rows);

        for (int i = 0; i < _rows.Length; i++)
        {
            _rows[i] = new Label();
            rows.AddChild(_rows[i]);
        }
    }

    private void RefreshHint()
    {
        if (_rows[0] == null) return;

        TowerPlacementManager.BuildMode mode =
            PlacementManager?.Mode ?? TowerPlacementManager.BuildMode.Pan;

        List<TowerDef> towers = Towers();
        string turretName = towers.Count > 1
            ? $"Turrets ({towers[Mathf.Clamp(_towerIndex, 0, towers.Count - 1)].DisplayName})"
            : "Turrets";

        SetRow(0, mode == TowerPlacementManager.BuildMode.Pan,     "1  Pan — drag to pan");
        SetRow(1, mode == TowerPlacementManager.BuildMode.Turrets, $"2  {turretName} — LMB place · RMB destroy");
        SetRow(2, mode == TowerPlacementManager.BuildMode.Edit,    "3  Edit — LMB open crystals");
        SetRow(3, mode == TowerPlacementManager.BuildMode.Wards,   "4  Wards — LMB lay · RMB lift");
    }

    private void SetRow(int index, bool active, string text) =>
        _rows[index].Text = (active ? "> " : "   ") + text;

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
    /// whether one arrives. So it hangs off the scene's <c>WhenPaused</c> branch instead, and this
    /// class only drives it.
    /// </summary>
    private void OnTowerEditRequested(Node2D tower)
    {
        if (LatticeEditor == null)
        {
            GD.PushWarning("[lattice] TowerPlacementUI has no LatticeEditor assigned");
            return;
        }

        if (tower is not TurretTower turret) return;
        if (turret.Lattice == null)
        {
            GD.Print("[lattice] that tower has no crystal lattice");
            return;
        }

        _editing = turret;

        LatticeEditor.Visible = true;
        LatticeEditor.Edit(turret.Lattice, turret.DisplayName, turret.CoreEnergy);
        GetTree().Paused = true;
    }

    private void CloseLatticeEditor()
    {
        GetTree().Paused = false;
        if (LatticeEditor != null) LatticeEditor.Visible = false;

        // the lattice was edited in place, so the tower already holds the new shot
        _editing?.Recompile();
        _editing = null;
        // stays in edit mode: the next turret is one click away
    }

    // ── Signal handler ───────────────────────────────────────────────────────────

    private void OnDimensionSwapped(bool pocketIsMain)
    {
        Visible = pocketIsMain;
        // the editor is not below this node, so it does not hide with it — close it explicitly
        if (!pocketIsMain && LatticeEditor is { Visible: true }) CloseLatticeEditor();
    }
}
