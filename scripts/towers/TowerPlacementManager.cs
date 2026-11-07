using System;
using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.camera;
using towerdefensegame.scripts.world;
using towerdefensegame.scripts.world.paths;
using towerdefensegame.scripts.world.enemies;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Manages the build-mode state machine inside the pocket dimension. Which mode is active is
/// chosen by number key in <see cref="TowerPlacementUI"/>; this node owns what the mouse does in
/// each.
///
/// Modes (<see cref="BuildMode"/>):
///   Pan      [1] — the mouse belongs to the camera: left-drag pans. Nothing here handles a click.
///   Turrets  [2] — ghost follows the snapped mouse. Left-click places (and stays, for chaining);
///                  right-click destroys the tower under the cursor.
///   Edit     [3] — left-click on a tower asks to open its crystal lattice
///                  (<see cref="TowerEditRequested"/>); the UI decides what that looks like.
///   Wards    [4] — left-click lays a ward on the snapped tile; right-click lifts the one there.
///                  The route itself lives on <see cref="Path"/>; this node only routes clicks to
///                  it (<c>impl-planning/combat/paths.md</c>).
///
/// Placing and destroying share a mode because they share a subject: the left button makes the
/// thing and the right button unmakes it, in turrets and wards alike.
///
/// Escape, and the pocket becoming the mini viewport, return to Pan.
/// Input is ignored while the pocket dimension is the mini viewport (_inputEnabled = false).
/// </summary>
public partial class TowerPlacementManager : Node2D
{
    [Export] public CoordConfig Coords { get; set; }
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }
    [Export] public Node2D PlacedTowersContainer { get; set; }
    [Export] public PocketCameraController Camera { get; set; }

    /// <summary>
    /// The player's route. Optional: with none assigned, ward mode is simply unavailable and
    /// every other mode is unaffected.
    /// </summary>
    [Export] public MiragePath Path { get; set; }

    /// <summary>Extra cushion (px) added to the enemy's collision radius when
    /// testing whether an enemy blocks placement. Prevents fast enemies from
    /// slipping under a freshly placed tower and keeps a small no-place buffer
    /// around them for game feel.</summary>
    [Export] public float EnemyClearancePx { get; set; } = 20f;

    /// <summary>What the mouse does. Numbered as the keys that select them.</summary>
    public enum BuildMode { Pan, Turrets, Edit, Wards }

    private BuildMode _mode;
    private TowerDef _pending;
    private Node2D   _ghost;
    private bool     _inputEnabled; // true only when pocket dimension is the main viewport

    private static readonly Color ValidColor   = new(1.0f, 1.0f, 1.0f, 0.6f);
    private static readonly Color InvalidColor = new(1.0f, 0.25f, 0.25f, 0.6f);

    public BuildMode Mode => _mode;

    /// <summary>The tower <see cref="BuildMode.Turrets"/> places, or null outside that mode.</summary>
    public TowerDef PendingTower => _pending;

    /// <summary>The mode or the pending tower changed. The mode hint redraws on it.</summary>
    public event Action? ModeChanged;

    /// <summary>Fired after a tower is successfully committed. Carries the tile footprint.</summary>
    public event Action<IReadOnlyList<Vector2I>>? TowerPlaced;

    /// <summary>Fired after a placed tower is removed. Carries the tile footprint it occupied.</summary>
    public event Action<IReadOnlyList<Vector2I>>? TowerRemoved;

    /// <summary>
    /// A tower was clicked in Editing mode. The manager deliberately stops here: opening the
    /// crystal editor is a UI decision, and this node stays free of any dependency on it.
    /// </summary>
    public event Action<Node2D>? TowerEditRequested;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Turret mode with the given tower: left-click places it, right-click destroys.</summary>
    public void BeginPlacement(TowerDef def)
    {
        if (def?.TowerScene == null) return;
        Leave();

        _mode    = BuildMode.Turrets;
        _pending = def;

        Node2D ghost = new() { Modulate = ValidColor };
        ghost.AddChild(new Sprite2D { Texture = def.PreviewTexture, Scale = new Vector2(2, 2) });
        if (def.TargetRadius > 0f)
            ghost.AddChild(BuildRadiusIndicator(def.TargetRadius));

        _ghost = ghost;
        AddChild(_ghost);

        if (Camera != null) Camera.EdgeScrollEnabled = true;
        ModeChanged?.Invoke();
    }

    /// <summary>Edit mode. Left-click on a tower asks to open its crystal lattice.</summary>
    public void BeginEditing()
    {
        Leave();
        _mode = BuildMode.Edit;
        ModeChanged?.Invoke();
    }

    /// <summary>Ward mode. Left-click lays a ward on the tile under the cursor; right-click lifts it.</summary>
    public void BeginWarding()
    {
        if (Path == null)
        {
            GD.PushWarning("[paths] no MiragePath assigned — ward mode does nothing");
            return;
        }

        Leave();
        _mode = BuildMode.Wards;
        if (Camera != null) Camera.EdgeScrollEnabled = true;
        ModeChanged?.Invoke();
    }

    /// <summary>Back to Pan from any mode. Discards any active ghost.</summary>
    public void Cancel()
    {
        BuildMode was = _mode;
        Leave();
        if (was != BuildMode.Pan) ModeChanged?.Invoke();
    }

    /// <summary>Tear down whatever the current mode put up, without announcing it.</summary>
    private void Leave()
    {
        Path?.SetPreview(null, false);
        _ghost?.QueueFree();
        _ghost   = null;
        _pending = null;
        _mode    = BuildMode.Pan;

        if (Camera != null) Camera.EdgeScrollEnabled = false;
    }

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
        if (_mode == BuildMode.Wards)
        {
            // The ghost for a ward is drawn by the path itself, not by a Node2D here: it has to
            // show the routed thread as well as the ring, and only the path can route.
            Vector2I hover = CoordHelper.WorldToTile(GetGlobalMousePosition(), Coords);
            Path?.SetPreview(hover, Path.CanWard(hover));
            return;
        }

        if (_mode != BuildMode.Turrets || _ghost == null || _pending == null) return;

        Vector2 mouseWorld = GetGlobalMousePosition();
        Vector2 snapped    = TowerSnapHelper.SnapCenter(mouseWorld, _pending.SizePixels, Coords);

        _ghost.GlobalPosition = snapped;

        List<Vector2I> footprint = new(TowerSnapHelper.FootprintTiles(snapped, _pending.SizePixels, Coords));
        bool valid = FootprintTracker.CanPlace(footprint)
                     && !AnyEnemyInFootprint(snapped, _pending.SizePixels)
                     && !AnyWardInFootprint(footprint);
        _ghost.Modulate = valid ? ValidColor : InvalidColor;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Pan leaves every click to the camera, which pans on a left-drag.
        if (!_inputEnabled || _mode == BuildMode.Pan) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    if (_mode == BuildMode.Turrets)    TryCommitPlacement();
                    else if (_mode == BuildMode.Edit)  TryEditAtMouse();
                    else if (_mode == BuildMode.Wards) LayWardAtMouse();
                    GetViewport().SetInputAsHandled();
                    break;

                case MouseButton.Right:
                    if (_mode == BuildMode.Turrets)    TryDestroyAtMouse();
                    else if (_mode == BuildMode.Wards) LiftWardAtMouse();
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

    /// <summary>
    /// Lay a ward on the tile under the cursor. <see cref="MiragePath.Add"/> refuses an occupied,
    /// unwalkable or already-warded tile itself, so a click on one is simply ignored — the preview
    /// has already been showing it red.
    /// </summary>
    private void LayWardAtMouse() =>
        Path?.Add(CoordHelper.WorldToTile(GetGlobalMousePosition(), Coords));

    /// <summary>Lift the ward under the cursor, if there is one. The route rejoins over the gap.</summary>
    private void LiftWardAtMouse() =>
        Path?.Remove(CoordHelper.WorldToTile(GetGlobalMousePosition(), Coords));

    /// <summary>
    /// Would this tower sit on the player's route? A tower is solid and cuts the nav mesh away
    /// under itself, so one dropped on a ward strands that waypoint, and one dropped across the
    /// thread forces a detour the player never chose. Refusing is kinder than silently rerouting
    /// them — the ward is the instruction, and the tower does not get to overrule it.
    /// </summary>
    private bool AnyWardInFootprint(IReadOnlyList<Vector2I> footprint)
    {
        if (Path == null) return false;

        for (int i = 0; i < footprint.Count; i++)
            if (Path.Crosses(footprint[i])) return true;

        return false;
    }

    private void TryCommitPlacement()
    {
        if (_pending == null || _ghost == null) return;

        Vector2 snapped          = _ghost.GlobalPosition;
        List<Vector2I> footprint = new(TowerSnapHelper.FootprintTiles(snapped, _pending.SizePixels, Coords));

        if (!FootprintTracker.CanPlace(footprint)) return;
        if (AnyEnemyInFootprint(snapped, _pending.SizePixels)) return;
        if (AnyWardInFootprint(footprint)) return;

        Node2D tower = _pending.TowerScene.Instantiate<Node2D>();
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

        // The ghost stays up: turret mode chains placements of the same type.
    }

    /// <summary>True if any enemy's inflated collision circle overlaps the
    /// world-space AABB of a tower with center <paramref name="snappedCenter"/>
    /// and size <paramref name="sizePixels"/>. Inflation = AgentRadius + EnemyClearancePx.</summary>
    private bool AnyEnemyInFootprint(Vector2 snappedCenter, Vector2I sizePixels)
    {
        Vector2 half = (Vector2)sizePixels * 0.5f;
        Vector2 min  = snappedCenter - half;
        Vector2 max  = snappedCenter + half;
        float   r    = GlobalEnemyConfig.AgentRadius + EnemyClearancePx;
        float   r2   = r * r;

        Viewport vp = GetViewport();
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Node2D n2d) continue;
            if (n2d.GetViewport() != vp) continue;
            Vector2 p = n2d.GlobalPosition;
            Vector2 closest = p.Clamp(min, max);
            if (p.DistanceSquaredTo(closest) <= r2) return true;
        }
        return false;
    }

    private void TryDestroyAtMouse()
    {
        Vector2 worldPos = GetGlobalMousePosition();
        Vector2I tile    = CoordHelper.WorldToTile(worldPos, Coords);
        if (FootprintTracker.TryGetTowerAt(tile, out Node2D tower))
            DestroyTower(tower);
    }

    /// <summary>Same lookup as destroy, different verb.</summary>
    private void TryEditAtMouse()
    {
        Vector2 worldPos = GetGlobalMousePosition();
        Vector2I tile    = CoordHelper.WorldToTile(worldPos, Coords);
        if (FootprintTracker.TryGetTowerAt(tile, out Node2D tower))
            TowerEditRequested?.Invoke(tower);
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
