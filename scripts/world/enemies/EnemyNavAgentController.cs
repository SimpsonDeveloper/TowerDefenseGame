using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Enemy variant that delegates pathfinding to Godot's built-in NavigationAgent2D.
/// Produces the highest-quality paths (optimal, navigates corridors) but requires
/// the scene to have a NavigationRegion2D with a baked navigation mesh.
///
/// See docs/pathfinding_nav_agent.md for full setup instructions.
/// </summary>
[GlobalClass]
public partial class EnemyNavAgentController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 10f;

    [ExportGroup("Navigation")]
    [Export] public NavigationAgent2D NavAgent;
    
    /// <summary>
    /// How close the agent must get to each path waypoint before it advances.
    /// Keep this at roughly half a tile width (8px for 16px tiles).
    /// </summary>
    [Export] public float PathDesiredDistance { get; set; } = 8f;

    /// <summary>
    /// How close the agent must get to the final target before navigation is
    /// considered finished.
    /// </summary>
    [Export] public float TargetDesiredDistance { get; set; } = 20f;

    /// <summary>
    /// Seconds between target-position updates sent to the NavigationAgent.
    /// Lowering this makes the enemy react faster to a moving target at the
    /// cost of more NavigationServer queries per second.
    /// </summary>
    [Export] public float TargetUpdateInterval { get; set; } = 0.15f;

    [ExportGroup("Target")]
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    /// <param name="delta">Physics delta in seconds.</param>
    /// <param name="distanceToTarget">Straight-line distance to target, or float.MaxValue.</param>
    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ────────────────────────────────────────────────────
    private Node2D _target;
    private float _targetUpdateTimer;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public sealed override void _Ready()
    {
        NavAgent = GetNode<NavigationAgent2D>("NavigationAgent2D");
        NavAgent.PathDesiredDistance = PathDesiredDistance;
        NavAgent.TargetDesiredDistance = TargetDesiredDistance;
        NavAgent.MaxSpeed = MoveSpeed;

        AddToGroup("enemies");
        ResolveTarget();
        OnReady();
    }

    public sealed override void _PhysicsProcess(double delta)
    {
        if (_target == null)
            ResolveTarget();
        
        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        // Periodically push the moving target's position to the nav agent
        _targetUpdateTimer -= (float)delta;
        if (_targetUpdateTimer <= 0f)
        {
            _targetUpdateTimer = TargetUpdateInterval;
            if (_target != null)
                NavAgent.TargetPosition = _target.GlobalPosition;
        }

        if (_target == null || NavAgent.IsNavigationFinished())
        {
            Velocity = Velocity.Lerp(Vector2.Zero, Acceleration * (float)delta);
            MoveAndSlide();
            OnPhysicsTick(delta, distToTarget);
            return;
        }

        Vector2 nextPos = NavAgent.GetNextPathPosition();
        Vector2 desiredVelocity = (nextPos - GlobalPosition).Normalized() * MoveSpeed;
        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetTarget(Node2D target)
    {
        _target = target;
        if (_target != null)
            NavAgent.TargetPosition = _target.GlobalPosition;
    }

    public void ClearTarget()
    {
        _target = null;
        Velocity = Vector2.Zero;
    }

    public Node2D Target => _target;

    // ── Internal ──────────────────────────────────────────────────────────

    private void ResolveTarget()
    {
        if (!string.IsNullOrEmpty(TargetGroup))
        {
            var viewport = GetViewport();
            foreach (Node node in GetTree().GetNodesInGroup(TargetGroup))
            {
                if (node is Node2D n2d && n2d.GetViewport() == viewport)
                {
                    _target = n2d;
                    break;
                }
            }
        }
        if (_target != null)
            NavAgent.TargetPosition = _target.GlobalPosition;
    }
}
