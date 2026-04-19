using Godot;
using towerdefensegame.scripts.camera;
using towerdefensegame.scripts.player;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Manages the dual-viewport system: overworld and pocket dimension.
/// Press the "swap_dimensions" action (default: Tab) to swap which world
/// is the main viewport and which is the mini viewport.
///
/// The mini viewport ignores all input. Both worlds continue to run
/// (physics, rendering) regardless of which is main.
/// </summary>
public partial class WorldManager : Node2D
{
    /// <summary>
    /// Emitted whenever the main/mini viewport swap occurs.
    /// pocketIsMain is true when the pocket dimension becomes the full-screen viewport.
    /// </summary>
    [Signal]
    public delegate void DimensionSwappedEventHandler(bool pocketIsMain);

    [Export] public SubViewportContainer OverworldContainer { get; set; }
    [Export] public SubViewportContainer PocketDimensionContainer { get; set; }
    [Export] public SubViewport OverworldViewport { get; set; }
    [Export] public SubViewport PocketDimensionViewport { get; set; }
    [Export] public PocketCameraController PocketDimensionCamera { get; set; }
    [Export] public PlayerCameraController OverworldCamera { get; set; }

    /// <summary>Fraction of window size used for the mini viewport (each axis).</summary>
    [Export] public float MiniViewportScale { get; set; } = 0.25f;

    private PlayerController _overworldPlayer;
    private bool _overworldIsMain = true;
    private bool _isDraggingMini;
    private Vector2 _lastDragPos;

    public override void _Ready()
    {
        GetViewport().SizeChanged += UpdateLayout;
        UpdateLayout();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= UpdateLayout;
    }

    public override void _Input(InputEvent @event)
    {
        SubViewportContainer miniContainer = _overworldIsMain ? PocketDimensionContainer : OverworldContainer;
        Rect2 miniRect = new Rect2(miniContainer.Position, miniContainer.Size);
        Vector2 mousePos = GetViewport().GetMousePosition();
        bool overMini = miniRect.HasPoint(mousePos);

        if (@event is InputEventMouseButton mb)
        {
            bool isScroll = mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown;
            if (overMini && isScroll && mb.IsPressed())
            {
                float dir = mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f;
                // Forward scroll to pocket camera only when it is the mini viewport;
                // either way consume the event so the main camera is not affected.
                if (_overworldIsMain && PocketDimensionCamera != null)
                {
                    PocketDimensionCamera.ApplyZoomStep(dir);
                    GetViewport().SetInputAsHandled();
                }
                else if (!_overworldIsMain && OverworldCamera != null)
                {
                    OverworldCamera.ApplyZoomStep(dir);
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && overMini && _overworldIsMain)
                {
                    _isDraggingMini = true;
                    _lastDragPos = mousePos;
                    GetViewport().SetInputAsHandled();
                }
                else if (!mb.Pressed && _isDraggingMini)
                {
                    _isDraggingMini = false;
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        else if (@event is InputEventMouseMotion motion && _isDraggingMini)
        {
            Vector2 delta = motion.Position - _lastDragPos;
            _lastDragPos = motion.Position;
            PocketDimensionCamera?.ApplyPan(-delta);
            GetViewport().SetInputAsHandled();
        }
        
        if (@event.IsActionPressed("swap_dimensions"))
        {
            _overworldIsMain = !_overworldIsMain;
            UpdateLayout();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Called via the PlayerSpawner.PlayerSpawned signal.</summary>
    public void OnPlayerSpawned(PlayerController player) => _overworldPlayer = player;

    private void UpdateLayout()
    {
        _isDraggingMini = false;
        Vector2 windowSize = GetViewport().GetVisibleRect().Size;
        Vector2 miniSize   = windowSize * MiniViewportScale;
        // Bottom-left corner
        Vector2 miniPos    = new Vector2(0, windowSize.Y - miniSize.Y);

        SubViewportContainer mainContainer = _overworldIsMain ? OverworldContainer : PocketDimensionContainer;
        SubViewportContainer miniContainer = _overworldIsMain ? PocketDimensionContainer : OverworldContainer;
        SubViewport mainViewport = _overworldIsMain ? OverworldViewport : PocketDimensionViewport;
        SubViewport miniViewport = _overworldIsMain ? PocketDimensionViewport : OverworldViewport;

        // Main container: full screen
        mainContainer.Position = Vector2.Zero;
        mainContainer.Size     = windowSize;
        mainContainer.ZIndex   = 0;

        // Mini container: bottom-left, scaled down
        miniContainer.Position = miniPos;
        miniContainer.Size     = miniSize;
        miniContainer.ZIndex   = 1; // draw on top of main

        // Both viewports handle input locally. The mini viewport's nodes have their
        // own input guards (e.g. PocketCameraController.InputEnabled) to stay inert.
        // Setting the mini to false would re-push events to the root viewport and
        // cause WorldManager._Input to fire twice for every event.
        mainViewport.HandleInputLocally = true;
        miniViewport.HandleInputLocally = true;

        // Disable PocketCamera's own drag handler when it is the mini viewport;
        // WorldManager drives it via ApplyPan in that case.
        if (PocketDimensionCamera != null)
            PocketDimensionCamera.InputEnabled = !_overworldIsMain;

        // Enable player movement only in active world (probably not needed, but keeping it as commented)
        // if (OverworldPlayer != null)
        //     OverworldPlayer.InputEnabled = _overworldIsMain;

        EmitSignal(SignalName.DimensionSwapped, !_overworldIsMain);
    }
}
