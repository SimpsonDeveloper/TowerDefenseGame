using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// Mirrors <c>BreakerComponent</c>'s zone-poll loop, but drives a
/// <see cref="HealthComponent"/> instead of a <c>Breakable</c>. Each physics
/// frame, scans the HitBox for a body in <see cref="TargetGroup"/> that owns a
/// HealthComponent child, and applies <see cref="Damage"/> on a fixed
/// interval while in contact.
/// </summary>
[GlobalClass]
public partial class AttackerComponent : Node2D
{
    /// <summary>DetectionZone used to detect nearby damageable bodies.</summary>
    [Export] public DetectionZone HitBox { get; set; }

    /// <summary>Only bodies in this group are considered. Empty = any body.</summary>
    [Export] public string TargetGroup { get; set; } = "Towers";

    /// <summary>HP removed per tick.</summary>
    [Export] public int Damage { get; set; } = 1;

    /// <summary>Seconds between damage ticks while in contact with a target.</summary>
    [Export] public float AttackInterval { get; set; } = 0.5f;

    private HealthComponent _currentTarget;
    private float _tickTimer;

    public override void _PhysicsProcess(double delta)
    {
        if (HitBox == null) return;

        UpdateTarget();
        if (_currentTarget == null) return;

        _tickTimer += (float)delta;
        if (_tickTimer >= AttackInterval)
        {
            _tickTimer = 0f;
            _currentTarget.TakeDamage(Damage);
        }
    }

    private void UpdateTarget()
    {
        HealthComponent found = null;
        foreach (var body in HitBox.GetOverlappingBodies())
        {
            if (!string.IsNullOrEmpty(TargetGroup) && !body.IsInGroup(TargetGroup)) continue;
            foreach (var child in body.GetChildren())
            {
                if (child is HealthComponent h && !h.IsDead)
                {
                    found = h;
                    break;
                }
            }
            if (found != null) break;
        }

        if (found == _currentTarget) return;

        // Target changed — disconnect old, connect new
        if (_currentTarget != null)
            _currentTarget.Destroyed -= OnTargetDestroyed;

        _currentTarget = found;
        _tickTimer = AttackInterval; // fire first tick on the next physics step

        if (_currentTarget != null)
            _currentTarget.Destroyed += OnTargetDestroyed;
    }

    private void OnTargetDestroyed()
    {
        if (_currentTarget != null)
            _currentTarget.Destroyed -= OnTargetDestroyed;
        _currentTarget = null;
        _tickTimer = 0f;
    }
}
