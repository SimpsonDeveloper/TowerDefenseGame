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
/// <b>Pushed, never polled.</b> A subclass subscribes to whatever announces a change and calls
/// <see cref="Refresh"/>. No <c>_Process</c> runs, so an idle unit costs nothing a frame, and every
/// bar on one unit updates by the same mechanism instead of each inventing its own.
///
/// A subclass is expected to subscribe in <c>_Ready</c>, <see cref="Refresh"/> once for the
/// starting value, and unsubscribe in <c>_ExitTree</c>. For that first read to be right, whatever
/// it reads has to be correct before any <c>_Ready</c> runs — sibling order is not guaranteed. Both
/// sources here manage it: <see cref="HealthComponent.Hp"/> reads full until something moves it,
/// and an <c>EnemyState</c> is built on construction.
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

    /// <summary>Last drawn fill, or -1 before the first refresh.</summary>
    private float _shown = -1f;

    public override void _Ready() => Position = Offset;

    /// <summary>
    /// Re-read the stat and redraw if it moved. Subclasses call this from whatever change
    /// notification they subscribe to, and once in <c>_Ready</c> for the starting value.
    /// </summary>
    protected void Refresh()
    {
        if (!TryGetFraction(out float fraction))
        {
            Visible = false;
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
