using Godot;

namespace towerdefensegame.scripts.camera;

/// <summary>
/// Free-pan camera for the pocket dimension.
/// WASD / arrow keys to pan, scroll wheel to zoom.
/// InputEnabled is toggled by WorldManager — when false (mini viewport) the
/// camera ignores all keyboard input so only the active world moves.
/// </summary>
public partial class PocketCameraController : Camera2D
{
    [Export] public float ZoomSpeed { get; set; } = 0.1f;
    [Export] public float MinZoom { get; set; } = 0.5f;
    [Export] public float MaxZoom { get; set; } = 4.0f;
    [Export] public bool UseSmoothing { get; set; }
    [Export] public bool SnapZoom { get; set; } = true;
    [Export] public float EdgeScrollMargin { get; set; } = 24f;
    [Export] public float EdgeScrollSpeed { get; set; } = 600f;

    public bool InputEnabled { get; set; } = true;

    private float _targetZoom;
    private bool _isDragging;
    private Vector2 _lastMousePos;

    public override void _Ready()
    {
        _targetZoom = Zoom.X;
    }

    public override void _Process(double delta)
    {
        if (UseSmoothing)
        {
            float currentZoom = Mathf.Lerp(Zoom.X, _targetZoom, 10f * (float)delta);
            Zoom = new Vector2(currentZoom, currentZoom);
        }
        else
        {
            Zoom = new Vector2(_targetZoom, _targetZoom);
        }

        if (InputEnabled && !_isDragging)
            ApplyEdgeScroll((float)delta);
    }

    private void ApplyEdgeScroll(float delta)
    {
        Viewport vp = GetViewport();
        Vector2 mousePos = vp.GetMousePosition();
        Vector2 size = vp.GetVisibleRect().Size;

        // Ignore if mouse is outside the viewport (e.g. window unfocused).
        if (mousePos.X < 0 || mousePos.Y < 0 || mousePos.X > size.X || mousePos.Y > size.Y)
            return;

        Vector2 dir = Vector2.Zero;
        if (mousePos.X < EdgeScrollMargin)          dir.X = -1f;
        else if (mousePos.X > size.X - EdgeScrollMargin) dir.X =  1f;
        if (mousePos.Y < EdgeScrollMargin)          dir.Y = -1f;
        else if (mousePos.Y > size.Y - EdgeScrollMargin) dir.Y =  1f;

        if (dir != Vector2.Zero)
            ApplyPan(dir * EdgeScrollSpeed * delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _isDragging = mb.Pressed;
                _lastMousePos = mb.Position;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _isDragging:
                ApplyPan(-(motion.Position - _lastMousePos));
                _lastMousePos = motion.Position;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } mb:
                ApplyZoomStep(mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>Zoom by direction: +1 = zoom in, -1 = zoom out.</summary>
    public void ApplyZoomStep(float direction)
    {
        if (SnapZoom)
            _targetZoom *= direction > 0 ? 2f : 0.5f;
        else
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
