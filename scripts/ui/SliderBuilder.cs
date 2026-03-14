using Godot;
using towerdefensegame;

/// <summary>
/// Builds labeled HSlider widgets arranged on a two-column grid inside any Container.
/// </summary>
public static class SliderBuilder
{
    // ── Widget dimensions ─────────────────────────────────────────────────────

    /// <summary>Size of each HSlider control.</summary>
    public static readonly Vector2 SliderSize = new(200, 16);

    /// <summary>Size of the label that sits above each slider.</summary>
    public static readonly Vector2 LabelSize = new(120, 23);

    /// <summary>
    /// Position of the label relative to its parent slider.
    /// Negative Y lifts the label above the slider track.
    /// </summary>
    public static readonly Vector2 LabelOffset = new(0, -24);

    // ── Grid layout ───────────────────────────────────────────────────────────

    /// <summary>
    /// Horizontal distance between column origins.
    /// SliderSize.X (200) + 47px inter-column gap.
    /// </summary>
    private const float ColumnSpacing = 247f;

    /// <summary>
    /// Vertical distance between row origins.
    /// SliderSize.Y (16) + LabelSize.Y (23) + 14px inter-row gap.
    /// </summary>
    private const float RowSpacing = 53f;

    /// <summary>Left margin from the container edge to column 0.</summary>
    private const float LeftMargin = 8f;

    /// <summary>Top margin from the container edge to row 0.</summary>
    private const float TopMargin = 24f;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HSlider with a label above it, adds both to <paramref name="parent"/>,
    /// and returns the label so callers can update its text on value changes.
    /// </summary>
    public static Label AddSlider(
        Container parent,
        string name, float min, float max, float step, float initialValue,
        int row, int col,
        Range.ValueChangedEventHandler callback)
    {
        var slider = new HSlider
        {
            Size = SliderSize,
            Position = new Vector2(LeftMargin + ColumnSpacing * col, TopMargin + RowSpacing * row),
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = initialValue,
        };
        slider.ValueChanged += callback;
        parent.AddChild(slider);

        var label = new Label
        {
            Size = LabelSize,
            Position = LabelOffset,
            Text = FormatLabel(name, initialValue),
        };
        var labelSettings = new LabelSettings();
        labelSettings.SetFontColor(new Color(0, 0, 0, 1));
        label.LabelSettings = labelSettings;
        slider.AddChild(label);

        return label;
    }

    /// <summary>Convenience overload that reads dimensions from a <see cref="SliderConfig"/>.</summary>
    public static Label AddSlider(
        Container parent, SliderConfig config,
        int row, int col,
        Range.ValueChangedEventHandler callback)
        => AddSlider(parent, config.Name, config.Min, config.Max, config.Step, config.InitialValue, row, col, callback);

    /// <summary>Formats a name/value pair for display in a slider label.</summary>
    public static string FormatLabel(string name, float value) => $"{name}: {value:0.####}";
}
