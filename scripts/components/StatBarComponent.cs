using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// A centred two-rect bar above its owner, drawn from one fraction. Subclass it and say where the
/// fraction comes from; everything else — size, offset, colours, hiding at full, redrawing only on
/// change — is handled here.
///
/// <b>Colour is the label.</b> Nothing on these bars is written in text, so a new bar earns its own
/// colour and that is how the player tells it apart. Stacking is by <see cref="Offset"/>: each bar
/// is an independent node and none of them knows what sits below it.
///
/// Polled rather than signalled, unlike <see cref="HealthBarComponent"/>: the values these track
/// are plain numbers on a plain object with no change notification. The poll is a comparison, and a
/// redraw only happens when the fraction actually moves.
/// </summary>
public abstract partial class StatBarComponent : Node2D
{
    /// <summary>Bar size in pixels (width × height).</summary>
    [Export] public Vector2 Size { get; set; } = new(32f, 3f);

    /// <summary>Local offset from the owner. Negative Y places the bar above.</summary>
    [Export] public Vector2 Offset { get; set; } = new(0f, -46f);

    [Export] public Color FillColor { get; set; } = new(1f, 1f, 1f);
    [Export] public Color BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.6f);

    /// <summary>
    /// If true, the bar is hidden while the stat is untouched, so clean units stay clean. Right for
    /// a bar where full means healthy; wrong for one where full means dangerous — see
    /// <c>MindBarComponent</c>, which turns it off on the type.
    /// </summary>
    [Export] public bool HideWhenFull { get; set; } = true;

    /// <summary>
    /// The bar's current fill, 0 to 1. Return false when there is nothing to show at all — no
    /// source wired, or a stat the enemy does not have — which hides the bar rather than drawing
    /// an empty one.
    /// </summary>
    protected abstract bool TryGetFraction(out float fraction);

    /// <summary>Last drawn fill, or -1 before the first poll. Only a change redraws.</summary>
    private float _shown = -1f;

    public override void _Ready() => Position = Offset;

    public override void _Process(double delta)
    {
        if (!TryGetFraction(out float fraction))
        {
            if (Visible) Visible = false;
            return;
        }

        Visible = !(HideWhenFull && fraction >= 1f);

        if (Mathf.IsEqualApprox(fraction, _shown)) return;

        _shown = fraction;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_shown < 0f) return;

        Vector2 origin = new(-Size.X / 2f, 0f);
        DrawRect(new Rect2(origin, Size), BackgroundColor, true);
        DrawRect(new Rect2(origin, new Vector2(Size.X * _shown, Size.Y)), FillColor, true);
    }
}
