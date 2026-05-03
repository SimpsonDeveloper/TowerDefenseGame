using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// Draws a centred two-rect HP bar above the owner. Subscribes to
/// <see cref="HealthComponent.Damaged"/> for redraws and to
/// <see cref="HealthComponent.Destroyed"/> to hide on death. Hidden at full HP
/// by default so undamaged units stay clean.
/// </summary>
[GlobalClass]
public partial class HealthBarComponent : Node2D
{
    [Export] public HealthComponent Health { get; set; }

    /// <summary>Bar size in pixels (width × height).</summary>
    [Export] public Vector2 Size { get; set; } = new(20f, 3f);

    /// <summary>Local offset from the owner. Negative Y places the bar above.</summary>
    [Export] public Vector2 Offset { get; set; } = new(0f, -12f);

    [Export] public Color FillColor { get; set; } = new(0.85f, 0.2f, 0.2f);
    [Export] public Color BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.6f);

    /// <summary>If true, the bar is hidden until the first damage tick.</summary>
    [Export] public bool HideAtFullHp { get; set; } = true;

    public override void _Ready()
    {
        Position = Offset;
        if (Health == null)
        {
            GD.PushWarning($"{Name}: Health not assigned — bar will not update.");
            return;
        }
        Health.Damaged += OnDamaged;
        Health.Destroyed += OnDestroyed;
        // HealthComponent guarantees Hp == MaxHp at start, so this is correct
        // regardless of _Ready ordering between the two children.
        Visible = !HideAtFullHp;
    }

    public override void _ExitTree()
    {
        if (Health != null)
        {
            Health.Damaged -= OnDamaged;
            Health.Destroyed -= OnDestroyed;
        }
    }

    public override void _Draw()
    {
        if (Health == null || Health.MaxHp <= 0) return;
        float pct = Mathf.Clamp((float)Health.Hp / Health.MaxHp, 0f, 1f);
        Vector2 origin = new(-Size.X / 2f, 0f);
        DrawRect(new Rect2(origin, Size), BackgroundColor, true);
        DrawRect(new Rect2(origin, new Vector2(Size.X * pct, Size.Y)), FillColor, true);
    }

    private void OnDamaged(int amount, int hp)
    {
        Visible = true;
        QueueRedraw();
    }

    private void OnDestroyed()
    {
        Visible = false;
    }
}
