using Godot;

namespace towerdefensegame;

/// <summary>
/// Reusable base class for free-roaming enemies with context-steering pathfinding.
///
/// Context steering scores radial ray directions each tick: each direction earns
/// interest for pointing toward the target and loses weight for pointing into an
/// obstacle. The enemy moves in the highest-scoring unblocked direction, giving
/// smooth obstacle avoidance without any navigation mesh setup.
///
/// Works in both the Overworld and Pocket Dimension because GetWorld2D() scopes
/// raycasts to the SubViewport the enemy lives in.
///
/// To create a specific enemy type, extend this class and override the virtual
/// hook methods — do NOT override _Ready or _PhysicsProcess directly.
/// </summary>
[GlobalClass]
public partial class EnemyController : CharacterBody2D
{
    // ── Movement ──────────────────────────────────────────────────────────

    /// <summary>Top speed in pixels/second.</summary>
    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;

    /// <summary>
    /// Velocity interpolation factor per second (higher = snappier turns).
    /// 8 gives smooth acceleration; 20+ feels instant.
    /// </summary>
    [Export] public float Acceleration { get; set; } = 8f;

    /// <summary>Distance in pixels at which the enemy stops closing in on the target.</summary>
    [Export] public float StopDistance { get; set; } = 20f;

    // ── Steering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Number of rays cast per steering update.
    /// 8 is a good default; reduce to 4 for very cheap enemies, increase to 16
    /// for better cornering around tight obstacles.
    /// </summary>
    [ExportGroup("Steering")]
    [Export] public int RayCount { get; set; } = 8;

    /// <summary>How far each ray travels to probe for obstacles (pixels).</summary>
    [Export] public float RayLength { get; set; } = 48f;

    /// <summary>
    /// Seconds between full steering recalculations.
    /// Enemies stagger their first update automatically so a group of 50 enemies
    /// spawned at the same time does not spike in the same frame.
    /// </summary>
    [Export] public float UpdateInterval { get; set; } = 0.12f;

    // ── Target ────────────────────────────────────────────────────────────

    /// <summary>
    /// Explicit target node. Takes priority over TargetGroup when set.
    /// Assign at runtime via SetTarget() or leave empty to use group lookup.
    /// </summary>
    [ExportGroup("Target")]
    [Export] public NodePath TargetPath { get; set; }

    /// <summary>
    /// Group name searched when TargetPath is not set.
    /// Add your player node to the "player" group, or use any custom group name.
    /// </summary>
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Runtime state ─────────────────────────────────────────────────────

    private Node2D _target;
    private Vector2 _desiredVelocity = Vector2.Zero;
    private float _updateTimer;

    // Allocated once — avoids per-frame garbage on the managed heap
    private float[] _interest;
    private float[] _danger;
    private Vector2[] _rayDirs;

    // Distributes initial steering updates across frames when many enemies spawn together
    private static int _globalStaggerCounter;

    // ── Virtual hooks (override these in subclasses) ───────────────────────

    /// <summary>
    /// Called at the end of _Ready, after the target is resolved and internal
    /// buffers are initialised. Cache node references here.
    /// </summary>
    protected virtual void OnReady() { }

    /// <summary>
    /// Called every physics frame after movement is applied.
    /// Use this for attack logic, animation state, or custom behaviour.
    /// </summary>
    /// <param name="delta">Physics delta in seconds.</param>
    /// <param name="distanceToTarget">
    /// Current distance to the target in pixels, or <see cref="float.MaxValue"/> if
    /// no target is set.
    /// </param>
    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override sealed void _Ready()
    {
        // Pre-compute fixed ray directions (unit circle, evenly spaced)
        _interest = new float[RayCount];
        _danger   = new float[RayCount];
        _rayDirs  = new Vector2[RayCount];
        for (int i = 0; i < RayCount; i++)
            _rayDirs[i] = Vector2.Right.Rotated(i * Mathf.Tau / RayCount);

        // Stagger this enemy's first update across UpdateInterval so a batch of
        // enemies spawning together doesn't all raycast in the same physics frame
        int staggerSteps = Mathf.Max(1, Mathf.RoundToInt(UpdateInterval / 0.016f));
        _updateTimer = (_globalStaggerCounter % staggerSteps) * 0.016f;
        _globalStaggerCounter++;

        AddToGroup("enemies");

        ResolveTarget();
        OnReady();
    }

    public override sealed void _PhysicsProcess(double delta)
    {
        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        _updateTimer -= (float)delta;
        if (_updateTimer <= 0f)
        {
            _updateTimer = UpdateInterval;
            RecalculateSteering(distToTarget);
        }

        Velocity = Velocity.Lerp(_desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        OnPhysicsTick(delta, distToTarget);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Swap the enemy's target at runtime (e.g. switch from player to a tower).</summary>
    public void SetTarget(Node2D target) => _target = target;

    /// <summary>Clear the current target; the enemy will stop moving.</summary>
    public void ClearTarget()
    {
        _target = null;
        _desiredVelocity = Vector2.Zero;
    }

    /// <summary>The currently tracked target, or null if none.</summary>
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

    private void RecalculateSteering(float distToTarget)
    {
        if (_target == null || distToTarget <= StopDistance)
        {
            _desiredVelocity = Vector2.Zero;
            return;
        }

        Vector2 toTarget = (_target.GlobalPosition - GlobalPosition).Normalized();
        var spaceState = GetWorld2D().DirectSpaceState;

        for (int i = 0; i < RayCount; i++)
        {
            // Interest: cosine similarity between ray and direction to target (clamped positive)
            _interest[i] = Mathf.Max(0f, _rayDirs[i].Dot(toTarget));

            // Danger: 1 if the ray hits any physics body in this world's collision space
            var query = PhysicsRayQueryParameters2D.Create(
                GlobalPosition,
                GlobalPosition + _rayDirs[i] * RayLength,
                CollisionMask
            );
            query.Exclude = [GetRid()];

            _danger[i] = spaceState.IntersectRay(query).Count > 0 ? 1f : 0f;
        }

        // Accumulate weighted directions, skipping fully blocked ones
        Vector2 chosen = Vector2.Zero;
        for (int i = 0; i < RayCount; i++)
        {
            float weight = _interest[i] - _danger[i];
            if (weight > 0f)
                chosen += _rayDirs[i] * weight;
        }

        // If every forward direction is blocked, push straight toward the target as
        // a last resort (lets the physics engine handle the slide)
        if (chosen == Vector2.Zero)
            chosen = toTarget;

        _desiredVelocity = chosen.Normalized() * MoveSpeed;
    }
}
