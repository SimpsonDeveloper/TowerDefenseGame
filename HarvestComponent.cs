using Godot;

/// <summary>
/// Area2D attached to a character that detects and ticks any Harvestable it touches.
/// Polls overlapping bodies each physics frame for reliable StaticBody2D detection.
/// </summary>
public partial class HarvestComponent : Area2D
{
    /// <summary>Seconds between harvest ticks while in contact with a harvestable.</summary>
    [Export]
    public float HarvestTickInterval { get; set; } = 0.5f;

    private Harvestable _currentTarget;
    private float _tickTimer;

    public override void _PhysicsProcess(double delta)
    {
        UpdateTarget();

        if (_currentTarget == null)
            return;

        _tickTimer += (float)delta;
        if (_tickTimer >= HarvestTickInterval)
        {
            _tickTimer = 0f;
            _currentTarget.ApplyHarvestTick();
        }
    }

    private void UpdateTarget()
    {
        Harvestable found = null;
        foreach (var body in GetOverlappingBodies())
        {
            if (body is Harvestable harvestable)
            {
                found = harvestable;
                break;
            }
        }

        if (found == _currentTarget)
            return;

        // Target changed — disconnect from old, connect to new
        if (_currentTarget != null)
            _currentTarget.Broken -= OnTargetBroken;

        _currentTarget = found;
        _tickTimer = HarvestTickInterval; // fire first tick on the next physics step

        if (_currentTarget != null)
            _currentTarget.Broken += OnTargetBroken;
    }

    private void OnTargetBroken()
    {
        _currentTarget.Broken -= OnTargetBroken;
        _currentTarget = null;
        _tickTimer = 0f;
    }
}
