using Godot;

namespace towerdefensegame;

/// <summary>
/// Enemy variant that samples a pre-computed FlowFieldManager each frame.
/// Per-enemy CPU cost is a single dictionary lookup — ideal for very large swarms
/// (100–500+) all chasing the same target.
///
/// Requires FlowFieldManager to be present in the same scene (group "flow_field_manager").
/// The FlowFieldManager must have its target set (usually the player).
///
/// Falls back to direct steering when outside the field radius.
/// See docs/pathfinding_flow_field.md for full setup instructions.
/// </summary>
[GlobalClass]
public partial class EnemyFlowFieldController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 8f;

    /// <summary>Stop moving when this close to the field origin (the target).</summary>
    [Export] public float StopDistance { get; set; } = 20f;

    [ExportGroup("Target")]
    /// <summary>
    /// Used only for the stop-distance check and the direct-steering fallback.
    /// The FlowFieldManager drives actual movement direction.
    /// </summary>
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ────────────────────────────────────────────────────

    private FlowFieldManager _flowField;
    private Node2D _target;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override sealed void _Ready()
    {
        _flowField = GetTree().GetFirstNodeInGroup("flow_field_manager") as FlowFieldManager;
        if (_flowField == null)
            GD.PushWarning($"{Name}: No FlowFieldManager found in group 'flow_field_manager'.");

        // Resolve target for stop-distance check and fallback
        var nodes = GetTree().GetNodesInGroup(TargetGroup);
        if (nodes.Count > 0)
            _target = nodes[0] as Node2D;

        AddToGroup("enemies");
        OnReady();
    }

    public override sealed void _PhysicsProcess(double delta)
    {
        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        Vector2 desiredVelocity = Vector2.Zero;

        if (_flowField != null && distToTarget > StopDistance)
        {
            Vector2 fieldDir = _flowField.Sample(GlobalPosition);

            if (fieldDir != Vector2.Zero)
            {
                // Inside the field — follow precomputed direction
                desiredVelocity = fieldDir * MoveSpeed;
            }
            else if (_target != null)
            {
                // Outside field radius — fall back to direct seek
                desiredVelocity = (_target.GlobalPosition - GlobalPosition).Normalized() * MoveSpeed;
            }
        }

        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetTarget(Node2D target) => _target = target;
    public Node2D Target => _target;
}
