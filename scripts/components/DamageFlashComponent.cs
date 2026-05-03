using Godot;
using Godot.Collections;

namespace towerdefensegame.scripts.components;

/// <summary>
/// Tints one or more CanvasItems on each <see cref="HealthComponent.Damaged"/>
/// pulse, then tweens each modulate back to its original value. Multiple
/// targets share one flash so a multi-sprite owner (e.g. tower base + turret)
/// stays visually in sync. Purely cosmetic — no game-state side effects.
/// </summary>
[GlobalClass]
public partial class DamageFlashComponent : Node
{
    [Export] public HealthComponent Health { get; set; }

    /// <summary>Sprites (or any CanvasItems) tinted on each flash.</summary>
    [Export] public Array<CanvasItem> Targets { get; set; } = new();

    /// <summary>Modulate value applied at the start of the flash. Default is a
    /// red tint since pure-white flashes need a shader (Modulate multiplies).</summary>
    [Export] public Color FlashColor { get; set; } = new(1.0f, 0.35f, 0.35f, 1.0f);

    /// <summary>Seconds to fade back to the base modulate.</summary>
    [Export] public float Duration { get; set; } = 0.12f;

    private Color[] _baseModulates;
    private Tween _tween;

    public override void _Ready()
    {
        if (Health == null || Targets == null || Targets.Count == 0)
        {
            GD.PushWarning($"{Name}: Health or Targets not assigned — flash disabled.");
            return;
        }

        _baseModulates = new Color[Targets.Count];
        for (int i = 0; i < Targets.Count; i++)
            _baseModulates[i] = Targets[i].Modulate;

        Health.Damaged += OnDamaged;
    }

    public override void _ExitTree()
    {
        if (Health != null)
            Health.Damaged -= OnDamaged;
    }

    private void OnDamaged(int amount, int hp)
    {
        _tween?.Kill();
        _tween = CreateTween().SetParallel(true);

        for (int i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            target.Modulate = FlashColor;
            _tween.TweenProperty(target, "modulate", _baseModulates[i], Duration);
        }
    }
}
