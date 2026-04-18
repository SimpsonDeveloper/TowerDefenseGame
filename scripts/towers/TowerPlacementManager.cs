using System.Collections.Generic;
using Godot;

namespace towerdefensegame;

/// <summary>
/// Manages the tower placement state machine inside the pocket dimension.
///
/// States:
///   Idle     — no tower selected; ghost is null.
///   Placing  — a tower type is selected; ghost follows the snapped mouse.
///
/// Transitions:
///   BeginPlacement(def)  → Placing  (called by TowerPlacementUI)
///   Left-click (valid)   → Placing  (commits tower, stays in Placing for chaining)
///   Right-click / Escape → Idle     (cancel)
///   DimensionSwapped(false) → Idle  (pocket became mini; placement cancelled)
///
/// Input is ignored while the pocket dimension is the mini viewport (_inputEnabled = false).
/// </summary>
public partial class TowerPlacementManager : Node2D
{
    [Export] public CoordConfig Coords { get; set; }
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }
    [Export] public Node2D PlacedTowersContainer { get; set; }

    private TowerDef _pending;
    private Node2D   _ghost;
    private bool     _inputEnabled; // true only when pocket dimension is the main viewport

    private static readonly Color ValidColor   = new(1.0f, 1.0f, 1.0f, 0.6f);
    private static readonly Color InvalidColor = new(1.0f, 0.25f, 0.25f, 0.6f);

    public bool IsPlacing => _pending != null;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Enter placing mode for the given tower definition.</summary>
    public void BeginPlacement(TowerDef def)
    {
        if (def?.TowerScene == null) return;
        CancelPlacement();

        _pending = def;
        _ghost   = new Sprite2D { Texture = def.PreviewTexture, Modulate = ValidColor };
        AddChild(_ghost);
    }

    /// <summary>Abort placement and discard the ghost.</summary>
    public void CancelPlacement()
    {
        _ghost?.QueueFree();
        _ghost   = null;
        _pending = null;
    }

    // ── Godot callbacks ─────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_ghost == null || _pending == null) return;

        Vector2 mouseWorld = GetGlobalMousePosition();
        Vector2 snapped    = TowerSnapHelper.SnapCenter(mouseWorld, _pending.SizePixels, Coords);

        _ghost.GlobalPosition = snapped;

        IEnumerable<Vector2I> footprint = TowerSnapHelper.FootprintTiles(snapped, _pending.SizePixels, Coords);
        _ghost.Modulate = FootprintTracker.CanPlace(footprint) ? ValidColor : InvalidColor;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inputEnabled || _pending == null) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    TryCommitPlacement();
                    GetViewport().SetInputAsHandled();
                    break;

                case MouseButton.Right:
                    CancelPlacement();
                    GetViewport().SetInputAsHandled();
                    break;
            }
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private void TryCommitPlacement()
    {
        if (_pending == null || _ghost == null) return;

        Vector2 snapped = _ghost.GlobalPosition;
        var footprint   = new List<Vector2I>(TowerSnapHelper.FootprintTiles(snapped, _pending.SizePixels, Coords));

        if (!FootprintTracker.CanPlace(footprint)) return;

        var tower = _pending.TowerScene.Instantiate<Node2D>();
        tower.GlobalPosition = snapped;
        PlacedTowersContainer.AddChild(tower);
        FootprintTracker.Register(footprint);

        // Stay in Placing mode so the user can chain placements of the same type.
        var def = _pending;
        CancelPlacement();
        BeginPlacement(def);
    }

    private void OnDimensionSwapped(bool pocketIsMain)
    {
        _inputEnabled = pocketIsMain;
        if (!pocketIsMain)
            CancelPlacement();
    }
}
