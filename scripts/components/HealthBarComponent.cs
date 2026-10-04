using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// The red HP bar above a unit. A <see cref="StatBarComponent"/> like the Mind bar beside it —
/// size, offset, colours, hiding at full and the drawing all live in the base; this supplies the
/// fraction and says what to listen to.
///
/// Listens to <see cref="HealthComponent.Changed"/> rather than <see cref="HealthComponent.Damaged"/>:
/// the bar cares that HP moved, not that it hurt. A future <c>Heal</c> is then correct here without
/// anyone remembering this class exists.
/// </summary>
[GlobalClass]
public partial class HealthBarComponent : StatBarComponent
{
    [Export] public HealthComponent Health { get; set; }

    /// <summary>Red, and smaller than the default bar, set on the type rather than in each scene.</summary>
    public HealthBarComponent()
    {
        Size = new Vector2(20f, 3f);
        Offset = new Vector2(0f, -12f);
        FillColor = new Color(0.85f, 0.2f, 0.2f);
    }

    public override void _Ready()
    {
        base._Ready();

        if (Health == null)
        {
            GD.PushWarning($"{Name}: Health not assigned — bar will not update.");
            return;
        }

        Health.Changed += OnChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Health != null) Health.Changed -= OnChanged;
    }

    /// <summary>A dead unit shows nothing — the base hides the bar when there is no fraction.</summary>
    protected override bool TryGetFraction(out float fraction)
    {
        fraction = 0f;

        if (Health == null || Health.MaxHp <= 0 || Health.IsDead) return false;

        fraction = Mathf.Clamp((float)(Health.Hp / Health.MaxHp), 0f, 1f);
        return true;
    }

    private void OnChanged(double hp) => Refresh();
}
