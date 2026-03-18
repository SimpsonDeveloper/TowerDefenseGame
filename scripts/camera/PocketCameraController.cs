using Godot;

namespace towerdefensegame;

/// <summary>
/// Free-pan camera for the pocket dimension.
/// WASD / arrow keys to pan, scroll wheel to zoom.
/// InputEnabled is toggled by WorldManager — when false (mini viewport) the
/// camera ignores all keyboard input so only the active world moves.
/// </summary>
public partial class PocketCameraController : Camera2D
{
    [Export] public float ZoomSpeed { get; set; } = 0.1f;
    [Export] public float MinZoom { get; set; } = 0.25f;
    [Export] public float MaxZoom { get; set; } = 4.0f;

    private float _targetZoom;
    private bool _isDragging;
    private Vector2 _lastMousePos;

    public override void _Ready()
    {
        _targetZoom = Zoom.X;
    }

    public override void _Process(double delta)
    {
        float currentZoom = Mathf.Lerp(Zoom.X, _targetZoom, 10f * (float)delta);
        Zoom = new Vector2(currentZoom, currentZoom);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _isDragging = mb.Pressed;
            _lastMousePos = mb.Position;
        }
        else if (@event is InputEventMouseMotion motion && _isDragging)
        {
            ApplyPan(-(motion.Position - _lastMousePos));
            _lastMousePos = motion.Position;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb)
            return;

        if (mb.ButtonIndex == MouseButton.WheelUp)
            _targetZoom += ZoomSpeed;
        else if (mb.ButtonIndex == MouseButton.WheelDown)
            _targetZoom -= ZoomSpeed;

        _targetZoom = Mathf.Clamp(_targetZoom, MinZoom, MaxZoom);
    }

    /// <summary>Called by WorldManager when scrolling over the mini viewport.</summary>
    public void ApplyZoomStep(float direction)
    {
        _targetZoom += direction * ZoomSpeed;
        _targetZoom = Mathf.Clamp(_targetZoom, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Called by WorldManager when click-dragging over the mini viewport.
    /// screenDelta is in screen pixels; converted to world units using current zoom.
    /// </summary>
    public void ApplyPan(Vector2 screenDelta)
    {
        Position += screenDelta / Zoom.X;
    }
}
