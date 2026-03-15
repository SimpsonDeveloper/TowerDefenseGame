using Godot;

/// <summary>
/// Handles the physical lifecycle of a harvest drop:
///   1. On spawn — applies a random burst velocity so drops scatter from the crystal.
///   2. Each frame — once the player enters the MagnetZone the drop accelerates toward
///      them continuously until picked up.
/// Requires its parent to be a RigidBody2D. Player collision is excluded so drops
/// pass through the player during scatter and magnetize.
/// </summary>
public partial class DropPhysics : Node
{
    [Export] public DetectionZone MagnetZone { get; set; }

    [Export] public float ScatterSpeedMin { get; set; } = 60f;
    [Export] public float ScatterSpeedMax { get; set; } = 140f;

    [Export] public float MagnetSpeed { get; set; } = 220f;

    private RigidBody2D         _body;
    private Node2D              _player;
    private bool                _magnetizing;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _body = GetParent<RigidBody2D>();
        _rng.Randomize();

        // Exclude the player from physics collision so drops pass through freely.
        var playerNode = GetTree().GetFirstNodeInGroup("Player");
        if (playerNode is PhysicsBody2D playerBody)
            _body.AddCollisionExceptionWith(playerBody);

        // Apply a random scatter impulse.
        float angle = _rng.RandfRange(0f, Mathf.Tau);
        float speed = _rng.RandfRange(ScatterSpeedMin, ScatterSpeedMax);
        _body.LinearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (MagnetZone == null)
            return;

        // Latch onto the player the first time they enter the magnet zone.
        if (!_magnetizing)
        {
            foreach (var body in MagnetZone.GetOverlappingBodies())
            {
                if (body is not PlayerController player)
                    continue;

                _magnetizing = true;
                _player      = player;
                break;
            }
        }

        // Once magnetizing, drive velocity toward the player every frame.
        if (_magnetizing && IsInstanceValid(_player))
        {
            var dir = (_player.GlobalPosition - _body.GlobalPosition).Normalized();
            _body.LinearVelocity = dir * MagnetSpeed;
        }
    }
}
