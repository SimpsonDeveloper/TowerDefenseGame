using Godot;

namespace towerdefensegame;

/// <summary>
/// Minimal enemy that moves in a straight line toward its target each frame.
/// No obstacle avoidance — relies entirely on physics collision to slide around
/// geometry. Best for flying/ghost enemies that conceptually ignore terrain.
///
/// This is the cheapest possible pathfinding variant. If enemies visually need to
/// bypass walls, use EnemyController (context steering) instead.
///
/// See docs/pathfinding_pure_seek.md for setup instructions.
/// </summary>
[GlobalClass]
public partial class EnemySeekController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 8f;

    /// <summary>Stop moving when this close to the target.</summary>
    [Export] public float StopDistance { get; set; } = 20f;

    [ExportGroup("Target")]
    [Export] public NodePath TargetPath { get; set; }
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ────────────────────────────────────────────────────

    private Node2D _target;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override sealed void _Ready()
    {
        AddToGroup("enemies");
        ResolveTarget();
        OnReady();
    }

    public override sealed void _PhysicsProcess(double delta)
    {
        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        Vector2 desiredVelocity = Vector2.Zero;

        if (_target != null && distToTarget > StopDistance)
            desiredVelocity = (_target.GlobalPosition - GlobalPosition).Normalized() * MoveSpeed;

        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetTarget(Node2D target) => _target = target;

    public void ClearTarget()
    {
        _target = null;
        Velocity = Vector2.Zero;
    }

    public Node2D Target => _target;

    // ── Internal ──────────────────────────────────────────────────────────

    private void ResolveTarget()
    {
        if (TargetPath != null && !TargetPath.IsEmpty)
        {
            _target = GetNodeOrNull<Node2D>(TargetPath);
            return;
        }
        if (!string.IsNullOrEmpty(TargetGroup))
        {
            var nodes = GetTree().GetNodesInGroup(TargetGroup);
            if (nodes.Count > 0)
                _target = nodes[0] as Node2D;
        }
    }
}
