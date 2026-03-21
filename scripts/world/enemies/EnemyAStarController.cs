using Godot;

namespace towerdefensegame;

/// <summary>
/// Enemy variant that follows waypoints produced by AStarGridManager.
/// Paths are optimal and guaranteed to navigate around any registered obstacle,
/// making this ideal for precision enemies or bosses that must never get stuck.
///
/// Requires AStarGridManager to be present in the same scene.
/// See docs/pathfinding_astar.md for full setup instructions.
/// </summary>
[GlobalClass]
public partial class EnemyAStarController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 10f;

    /// <summary>How close the enemy must get to a waypoint before advancing.</summary>
    [Export] public float WaypointReachedDistance { get; set; } = 10f;

    /// <summary>Stop moving when this close to the final target.</summary>
    [Export] public float StopDistance { get; set; } = 20f;

    [ExportGroup("Pathfinding")]
    /// <summary>
    /// Seconds between full A* path recomputes. Lower values track a moving
    /// target more accurately at the cost of more AStarGrid2D queries.
    /// </summary>
    [Export] public float PathUpdateInterval { get; set; } = 0.4f;

    /// <summary>
    /// If the target moves farther than this many pixels since the last path
    /// was computed, force an immediate recompute instead of waiting for the
    /// timer. Keeps the path fresh when the target is moving fast.
    /// </summary>
    [Export] public float PathInvalidationDistance { get; set; } = 64f;

    [ExportGroup("Target")]
    [Export] public NodePath TargetPath { get; set; }
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ────────────────────────────────────────────────────

    private AStarGridManager _gridManager;
    private Node2D _target;
    private Vector2[] _currentPath = System.Array.Empty<Vector2>();
    private int _waypointIndex;
    private float _pathUpdateTimer;
    private Vector2 _lastKnownTargetPos;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override sealed void _Ready()
    {
        _gridManager = GetTree().GetFirstNodeInGroup("astar_manager") as AStarGridManager;
        if (_gridManager == null)
            GD.PushWarning($"{Name}: No AStarGridManager found in group 'astar_manager'. Add one to the scene.");

        AddToGroup("enemies");
        ResolveTarget();
        OnReady();
    }

    public override sealed void _PhysicsProcess(double delta)
    {
        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        if (_target != null && _gridManager != null)
        {
            bool targetMovedFar = _target.GlobalPosition.DistanceTo(_lastKnownTargetPos)
                                  > PathInvalidationDistance;

            _pathUpdateTimer -= (float)delta;
            if (_pathUpdateTimer <= 0f || targetMovedFar)
            {
                _pathUpdateTimer = PathUpdateInterval;
                RecomputePath();
            }
        }

        FollowPath(delta);
        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetTarget(Node2D target)
    {
        _target = target;
        _pathUpdateTimer = 0f; // Force immediate recompute next frame
    }

    public void ClearTarget()
    {
        _target = null;
        _currentPath = System.Array.Empty<Vector2>();
        Velocity = Vector2.Zero;
    }

    public Node2D Target => _target;

    // ── Internal ──────────────────────────────────────────────────────────

    private void RecomputePath()
    {
        if (_target == null || _gridManager == null) return;

        _lastKnownTargetPos = _target.GlobalPosition;
        _currentPath = _gridManager.RequestPath(GlobalPosition, _target.GlobalPosition);
        _waypointIndex = 0;
    }

    private void FollowPath(double delta)
    {
        if (_currentPath.Length == 0 || _waypointIndex >= _currentPath.Length)
        {
            Velocity = Velocity.Lerp(Vector2.Zero, Acceleration * (float)delta);
            MoveAndSlide();
            return;
        }

        Vector2 waypoint = _currentPath[_waypointIndex];
        float distToWaypoint = GlobalPosition.DistanceTo(waypoint);

        // Check if we've reached the final waypoint (stop distance)
        bool isFinalWaypoint = _waypointIndex == _currentPath.Length - 1;
        float threshold = isFinalWaypoint ? StopDistance : WaypointReachedDistance;

        if (distToWaypoint <= threshold)
        {
            _waypointIndex++;
            if (_waypointIndex >= _currentPath.Length)
            {
                Velocity = Velocity.Lerp(Vector2.Zero, Acceleration * (float)delta);
                MoveAndSlide();
                return;
            }
            waypoint = _currentPath[_waypointIndex];
        }

        Vector2 desiredVelocity = (waypoint - GlobalPosition).Normalized() * MoveSpeed;
        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();
    }

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
