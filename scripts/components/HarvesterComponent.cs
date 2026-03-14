using Godot;

/// <summary>
/// Detects and ticks any Harvestable within range using a HitBoxComponent.
/// Polls the HitBox each physics frame for reliable StaticBody2D detection.
/// </summary>
public partial class HarvesterComponent : Node
{
    /// <summary>Seconds between harvest ticks while in contact with a harvestable.</summary>
    [Export] public float HarvestTickInterval { get; set; } = 0.5f;

    /// <summary>DetectionZone used to detect nearby Harvestables.</summary>
    [Export] public DetectionZone HitBox { get; set; }

    private Harvestable _currentTarget;
    private float       _tickTimer;

    public override void _PhysicsProcess(double delta)
    {
        if (HitBox == null)
            return;

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
        foreach (var body in HitBox.GetOverlappingBodies())
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
