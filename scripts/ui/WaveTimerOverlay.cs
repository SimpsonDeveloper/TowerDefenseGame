using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.ui;

/// <summary>
/// Root-level CanvasLayer that displays the pocket-dim spawner's wave countdown
/// and queue size. Lives above both viewports so it is visible regardless of
/// which dimension is currently primary.
/// </summary>
public partial class WaveTimerOverlay : CanvasLayer
{
    [Export] public PocketDimensionEnemySpawner Spawner { get; set; }
    [Export] public Vector2 Anchor { get; set; } = new Vector2(20, 20);
    [Export] public int FontSize { get; set; } = 20;

    private Label _label;

    public override void _Ready()
    {
        Layer = UiLayer.WaveTimer;
        _label = new Label { Position = Anchor };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (Spawner == null)
        {
            _label.Text = "(no spawner)";
            return;
        }

        int queued = Spawner.QueueSize;
        if (!Spawner.IsTimerActive)
            _label.Text = $"Queue: {queued}\nNext wave: —";
        else
            _label.Text = $"Queue: {queued}\nNext wave: {Spawner.SecondsRemaining,4:F1}s";
    }
}
