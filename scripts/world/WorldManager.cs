using Godot;

namespace towerdefensegame;

/// <summary>
/// Manages the dual-viewport system: overworld and pocket dimension.
/// Press the "swap_dimensions" action (default: Tab) to swap which world
/// is the main viewport and which is the mini viewport.
///
/// The mini viewport ignores all input. Both worlds continue to run
/// (physics, rendering) regardless of which is main.
/// </summary>
public partial class WorldManager : Node
{
    [Export] public SubViewportContainer OverworldContainer { get; set; }
    [Export] public SubViewportContainer PocketDimensionContainer { get; set; }
    [Export] public SubViewport OverworldViewport { get; set; }
    [Export] public SubViewport PocketDimensionViewport { get; set; }
    [Export] public PlayerController OverworldPlayer { get; set; }
    [Export] public PocketCameraController PocketDimensionCamera { get; set; }

    /// <summary>Fraction of window size used for the mini viewport (each axis).</summary>
    [Export] public float MiniViewportScale { get; set; } = 0.25f;

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
            bool isScroll = mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown;

            if (overMini && isScroll)
            {
                // Forward scroll to pocket camera only when it is the mini viewport;
                // either way consume the event so the main camera is not affected.
                if (_overworldIsMain && PocketDimensionCamera != null)
                {
                    float dir = mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f;
                    PocketDimensionCamera.ApplyZoomStep(dir);
                }
                GetViewport().SetInputAsHandled();
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
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("swap_dimensions"))
        {
            _overworldIsMain = !_overworldIsMain;
            UpdateLayout();
            GetViewport().SetInputAsHandled();
        }
    }

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

        // Route input only to the active world
        mainViewport.HandleInputLocally = true;
        miniViewport.HandleInputLocally = false;

        // Enable player movement only in active world
        if (OverworldPlayer != null)
            OverworldPlayer.InputEnabled = _overworldIsMain;
    }
}
