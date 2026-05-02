using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Manages the tower placement state machine inside the pocket dimension.
///
/// States:
///   Idle        — no mode active; ghost is null.
///   Placing     — a tower type is selected; ghost follows the snapped mouse,
///                 left-click commits.
///   Destroying  — left-click on a placed tower destroys it (routed through
///                 <see cref="ITowerPlaceable.Destroy"/>).
///
/// Transitions:
///   BeginPlacement(def)     → Placing     (TowerPlacementUI)
///   BeginDestroying()       → Destroying  (TowerPlacementUI)
///   Left-click (Placing)    → Placing     (commits, stays for chaining)
///   Left-click (Destroying) → Destroying  (destroys tower under cursor, stays)
///   Right-click / Escape    → Idle        (cancel)
///   DimensionSwapped(false) → Idle        (pocket became mini)
///
/// Input is ignored while the pocket dimension is the mini viewport (_inputEnabled = false).
/// </summary>
public partial class TowerPlacementManager : Node2D
{
    [Export] public CoordConfig Coords { get; set; }
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }
    [Export] public Node2D PlacedTowersContainer { get; set; }

    private enum Mode { Idle, Placing, Destroying }

    private Mode     _mode;
    private TowerDef _pending;
    private Node2D   _ghost;
    private bool     _inputEnabled; // true only when pocket dimension is the main viewport

    private static readonly Color ValidColor   = new(1.0f, 1.0f, 1.0f, 0.6f);
    private static readonly Color InvalidColor = new(1.0f, 0.25f, 0.25f, 0.6f);

    public bool IsPlacing    => _mode == Mode.Placing;
    public bool IsDestroying => _mode == Mode.Destroying;

    /// <summary>Fired after a tower is successfully committed. Carries the tile footprint.</summary>
    public event Action<IReadOnlyList<Vector2I>>? TowerPlaced;

    /// <summary>Fired after a placed tower is removed. Carries the tile footprint it occupied.</summary>
    public event Action<IReadOnlyList<Vector2I>>? TowerRemoved;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Enter placing mode for the given tower definition.</summary>
    public void BeginPlacement(TowerDef def)
    {
        if (def?.TowerScene == null) return;
        Cancel();

        _mode    = Mode.Placing;
        _pending = def;

        var ghost = new Node2D { Modulate = ValidColor };
        ghost.AddChild(new Sprite2D { Texture = def.PreviewTexture });
        if (def.TargetRadius > 0f)
            ghost.AddChild(BuildRadiusIndicator(def.TargetRadius));

        _ghost = ghost;
        AddChild(_ghost);
    }

    /// <summary>Enter destroying mode. Left-click on a tower destroys it.</summary>
    public void BeginDestroying()
    {
        Cancel();
        _mode = Mode.Destroying;
    }

    /// <summary>Return to Idle from any mode. Discards any active ghost.</summary>
    public void Cancel()
    {
        _ghost?.QueueFree();
        _ghost   = null;
        _pending = null;
        _mode    = Mode.Idle;
    }

    /// <summary>Back-compat alias — old call sites and the Cancel button still
    /// use this name.</summary>
    public void CancelPlacement() => Cancel();

    /// <summary>Programmatic destruction — UI, scripted events, and (eventually)
    /// enemy attack code all route through here. Safe to call on any tower that
    /// implements <see cref="ITowerPlaceable"/>; lifecycle cleanup runs via the
    /// tower's <c>Destroyed</c> event.</summary>
    public void DestroyTower(Node2D tower)
    {
        if (tower is ITowerPlaceable placeable)
            placeable.Destroy();
    }

    // ── Godot callbacks ─────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_mode != Mode.Placing || _ghost == null || _pending == null) return;

        Vector2 mouseWorld = GetGlobalMousePosition();
        Vector2 snapped    = TowerSnapHelper.SnapCenter(mouseWorld, _pending.SizePixels, Coords);

        _ghost.GlobalPosition = snapped;

        IEnumerable<Vector2I> footprint = TowerSnapHelper.FootprintTiles(snapped, _pending.SizePixels, Coords);
        _ghost.Modulate = FootprintTracker.CanPlace(footprint) ? ValidColor : InvalidColor;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inputEnabled || _mode == Mode.Idle) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    if (_mode == Mode.Placing)        TryCommitPlacement();
                    else if (_mode == Mode.Destroying) TryDestroyAtMouse();
                    GetViewport().SetInputAsHandled();
                    break;

                case MouseButton.Right:
                    Cancel();
                    GetViewport().SetInputAsHandled();
                    break;
            }
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Cancel();
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
        if (tower is ITowerPlaceable placeable)
        {
            placeable.Configure(_pending);
            // Subscribe before AddChild so we don't miss an early Destroy().
            placeable.Destroyed += OnTowerDestroyed;
        }
        PlacedTowersContainer.AddChild(tower);
        FootprintTracker.Register(tower, footprint);
        TowerPlaced?.Invoke(footprint);

        // Stay in Placing mode so the user can chain placements of the same type.
        var def = _pending;
        Cancel();
        BeginPlacement(def);
    }

    private void TryDestroyAtMouse()
    {
        Vector2 worldPos = GetGlobalMousePosition();
        Vector2I tile    = CoordHelper.WorldToTile(worldPos, Coords);
        if (FootprintTracker.TryGetTowerAt(tile, out var tower))
            DestroyTower(tower);
    }

    /// <summary>
    /// Handler for <see cref="ITowerPlaceable.Destroyed"/>. Captures the
    /// footprint tiles before <c>Unregister</c> wipes them, releases the slot,
    /// and fans out <see cref="TowerRemoved"/> so navmesh / reachability
    /// consumers can rebake. Runs regardless of who initiated destruction
    /// (UI, scripted, or — eventually — enemy attacks).
    /// </summary>
    private void OnTowerDestroyed(Node2D tower)
    {
        if (!FootprintTracker.TryGetFootprint(tower, out var fp)) return;
        var tiles = new List<Vector2I>(fp.Tiles);
        FootprintTracker.Unregister(tower);
        TowerRemoved?.Invoke(tiles);
    }

    private void OnDimensionSwapped(bool pocketIsMain)
    {
        _inputEnabled = pocketIsMain;
        if (!pocketIsMain) Cancel();
    }

    private static Line2D BuildRadiusIndicator(float radius)
    {
        const int segments = 64;
        var line = new Line2D
        {
            Width        = 1f,
            DefaultColor = Colors.Aqua,
        };
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.Tau / segments;
            line.AddPoint(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        return line;
    }
}
