using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace towerdefensegame;

/// <summary>
/// Enemy variant that navigates via raycasts and a chain of "last seen" checkpoints.
/// Cheaper than NavigationAgent2D — requires no nav mesh, no NavBaker, no PolygonTerrainManager data.
///
/// States
/// ──────
/// Waiting       — No target visible. Decelerates and waits until it gains direct line-of-sight.
/// Chasing       — Direct LoS to target. Moves straight toward it.
/// FollowingChain — Target hidden. Walks a chain of checkpoints (oldest first) while the
///                 most-recent checkpoint keeps raycasting toward the target to extend the chain.
///
/// Checkpoint chain layout
/// ───────────────────────
///   Enemy → C[0] (oldest) → C[1] → … → C[n] (active raycaster) ⟶ Target (current position)
///
/// • C[n] raycasts to Target every frame. On sight loss (after debounce) it records the last
///   clear-sight position as C[n+1] and becomes inert.
/// • If the enemy regains direct LoS to Target at any time, the chain is discarded.
/// • If the enemy's path to C[0] is blocked (terrain change, etc.) the chain is abandoned
///   and the enemy returns to Waiting.
///
/// Performance: max 3 PhysicsDirectSpaceState2D queries per enemy per frame.
/// </summary>
[GlobalClass]
public partial class EnemyRaycastController : CharacterBody2D
{
    // ── State ──────────────────────────────────────────────────────────────

    private enum NavState { Waiting, Chasing, FollowingChain }

    // ── Configuration ──────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 10f;

    /// <summary>
    /// Maximum distance at which the enemy (or a checkpoint) can see the target.
    /// </summary>
    [ExportGroup("Navigation")]
    [Export] public float SightRange { get; set; } = 400f;

    /// <summary>
    /// How close the enemy must get to a checkpoint position before it is considered reached.
    /// ~half a tile width is a reasonable default.
    /// </summary>
    [Export] public float CheckpointReachDistance { get; set; } = 16f;

    /// <summary>Safety cap on the checkpoint chain length.</summary>
    [Export] public int MaxCheckpoints { get; set; } = 100;

    /// <summary>
    /// Seconds the active raycaster must lose sight before a new checkpoint is recorded.
    /// Prevents checkpoint spam when the target hovers near a wall edge.
    /// </summary>
    [Export] public float LostSightDebounce { get; set; } = 0.15f;

    /// <summary>
    /// Collision mask for all LoS raycasts. Should include terrain/wall layers only —
    /// not the player, not other enemies.
    /// </summary>
    [Export] public uint RaycastCollisionMask { get; set; } = 1u;

    [ExportGroup("Steering")]
    /// <summary>Number of rays cast per steering update. 8 is a good default; increase to 16 for tighter corners.</summary>
    [Export] public int RayCount { get; set; } = 8;

    /// <summary>How far each danger ray travels to probe for obstacles (pixels).</summary>
    [Export] public float RayLength { get; set; } = 48f;

    /// <summary>
    /// Seconds between full steering recalculations. Enemies stagger their first
    /// update automatically so a batch spawning together doesn't spike the same frame.
    /// </summary>
    [Export] public float SteeringUpdateInterval { get; set; } = 0.12f;

    /// <summary>
    /// When true, draws raycasts and checkpoints each frame using distinct colors:
    ///   Lime     — direct LoS ray (enemy → target, sight clear)
    ///   OrangeRed — direct LoS ray (enemy → target, sight blocked / debouncing)
    ///   Yellow   — path integrity ray (enemy → next checkpoint)
    ///   Orange   — inert chain segment (checkpoint → checkpoint)
    ///   Cyan     — active raycaster ray (last checkpoint → target)
    ///   White    — inert checkpoint dot
    ///   Cyan     — active checkpoint dot (larger)
    ///   Magenta  — last-clear-sight position dot
    /// </summary>
    [ExportGroup("Debug")]
    [Export] public bool DebugDraw { get; set; } = false;

    [ExportGroup("Target")]
    [Export] public NodePath TargetPath { get; set; }
    [Export] public string TargetGroup { get; set; } = "Player";

    // ── Virtual hooks ──────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    /// <param name="delta">Physics delta in seconds.</param>
    /// <param name="distanceToTarget">Straight-line distance to target, or float.MaxValue.</param>
    protected virtual void OnPhysicsTick(double delta, float distanceToTarget) { }

    // ── Internal state ─────────────────────────────────────────────────────

    private NavState _state = NavState.Waiting;
    private NavState _prevState = NavState.Waiting;
    private Node2D _target;

    // Index 0 = oldest waypoint (next to walk toward); Last index = active raycaster.
    private readonly List<Vector2> _checkpoints = new();

    // Last target position where the active raycaster had clear LoS.
    private Vector2 _lastClearSightPos;

    // Seconds since the active raycaster lost sight of the target.
    private float _lostSightTimer;

    // Whether the active raycaster had LoS on the previous frame.
    private bool _activeRaycasterHadSight;

    // ── Context steering ───────────────────────────────────────────────────

    // The position the enemy is currently trying to reach (set each nav tick).
    private Vector2 _steeringDestination;

    // The velocity the steering system wants to achieve (applied every frame).
    private Vector2 _desiredVelocity;

    // Allocated once in _Ready — avoids per-frame heap allocation.
    private float[] _interest;
    private float[] _danger;
    private Vector2[] _rayDirs;

    private float _steeringTimer;
    private float _delta;
    private static int _globalStaggerCounter;

    // ── Debug draw state (written each physics frame, read in _Draw) ───────

    private Vector2 _dbgTargetPos;
    private bool _dbgEnemyCanSeeTarget;

    private readonly List<(Vector2 HitPos, string ColliderName)> _dbgHits = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public sealed override void _Ready()
    {
        _interest = new float[RayCount];
        _danger   = new float[RayCount];
        _rayDirs  = new Vector2[RayCount];
        for (int i = 0; i < RayCount; i++)
            _rayDirs[i] = Vector2.Right.Rotated(i * Mathf.Tau / RayCount);

        // Stagger first update so enemies spawning together don't all raycast the same frame.
        int staggerSteps = Mathf.Max(1, Mathf.RoundToInt(SteeringUpdateInterval / 0.016f));
        _steeringTimer = (_globalStaggerCounter % staggerSteps) * 0.016f;
        _globalStaggerCounter++;

        AddToGroup("enemies");
        ResolveTarget();
        OnReady();
    }

    public sealed override void _PhysicsProcess(double delta)
    {
        _delta = (float)delta;

        float distToTarget = _target != null
            ? GlobalPosition.DistanceTo(_target.GlobalPosition)
            : float.MaxValue;

        if (_target != null)
            NavigationTick();

        _steeringTimer -= (float)delta;
        if (_steeringTimer <= 0f)
        {
            _steeringTimer = SteeringUpdateInterval;
            RecalculateSteering();
        }

        Velocity = Velocity.Lerp(_desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        if (DebugDraw)
            QueueRedraw();
        OnPhysicsTick(delta, distToTarget);
    }

    // ── Navigation tick ────────────────────────────────────────────────────

    private void NavigationTick()
    {
        if (DebugDraw) _dbgHits.Clear();

        Vector2 targetPos = _target.GlobalPosition;
        bool enemySeesTarget = HasLineOfSight(GlobalPosition, targetPos, SightRange);

        // Cache for _Draw — physics fields must not be read on the render thread.
        _dbgTargetPos = targetPos;
        _dbgEnemyCanSeeTarget = enemySeesTarget;

        // Direct LoS always wins — discard the chain immediately and chase.
        if (enemySeesTarget && _state != NavState.Chasing)
        {
            _checkpoints.Clear();
            _lostSightTimer = 0f;
            _activeRaycasterHadSight = true;
            _lastClearSightPos = targetPos;
            _state = NavState.Chasing;
        }

        switch (_state)
        {
            case NavState.Waiting:
                break;

            case NavState.Chasing:
                TickChasing(targetPos, enemySeesTarget);
                break;

            case NavState.FollowingChain:
                TickFollowingChain(targetPos);
                break;
        }

        if (DebugDraw && _state != _prevState)
        {
            GD.Print($"[{Name}] {_prevState} → {_state}");
            _prevState = _state;
        }
    }

    private void TickChasing(Vector2 targetPos, bool canSee)
    {
        if (canSee)
        {
            _lastClearSightPos = targetPos;
            _lostSightTimer = 0f;
            _steeringDestination = targetPos;
            return;
        }

        // Lost direct sight — keep moving toward last known position while debouncing.
        _lostSightTimer += _delta;
        _steeringDestination = _lastClearSightPos;

        if (_lostSightTimer >= LostSightDebounce)
        {
            // _checkpoints is always empty when entering from Chasing, so this always fits.
            _checkpoints.Add(_lastClearSightPos);
            _activeRaycasterHadSight = false;
            _lostSightTimer = 0f;
            _state = NavState.FollowingChain;
        }
    }

    private void TickFollowingChain(Vector2 targetPos)
    {
        if (_checkpoints.Count == 0)
        {
            _state = NavState.Waiting;
            return;
        }

        // ── Path integrity check ──────────────────────────────────────────
        // If the enemy can't see its next waypoint, the path is broken (terrain changed,
        // enemy too wide, etc.). Abandon the chain and wait for direct LoS.
        if (!HasLineOfSight(GlobalPosition, _checkpoints[0]))
        {
            if (DebugDraw)
                GD.Print($"[{Name}] C[0] blocked at {_checkpoints[0]} — abandoning chain ({_checkpoints.Count} checkpoints)");
            AbandonChain();
            return;
        }

        // ── Walk toward the oldest checkpoint ─────────────────────────────
        _steeringDestination = _checkpoints[0];

        if (GlobalPosition.DistanceTo(_checkpoints[0]) <= CheckpointReachDistance)
        {
            _checkpoints.RemoveAt(0);

            if (_checkpoints.Count == 0)
            {
                // All checkpoints consumed. Try to pick up direct sight.
                if (HasLineOfSight(GlobalPosition, targetPos, SightRange))
                {
                    _lastClearSightPos = targetPos;
                    _state = NavState.Chasing;
                }
                else
                {
                    _state = NavState.Waiting;
                }
                return;
            }
        }

        // ── Active raycaster: C[Last] casts toward current target position ─
        Vector2 activePos = _checkpoints[_checkpoints.Count - 1];
        bool activeCanSee = HasLineOfSight(activePos, targetPos, SightRange);

        if (activeCanSee)
        {
            _lastClearSightPos = targetPos;
            _lostSightTimer = 0f;
            _activeRaycasterHadSight = true;
            return;
        }

        // Active raycaster lost sight — debounce, then extend the chain.
        if (_activeRaycasterHadSight)
            _activeRaycasterHadSight = false;

        _lostSightTimer += _delta;

        if (_lostSightTimer < LostSightDebounce)
            return;

        _lostSightTimer = 0f;

        if (_checkpoints.Count >= MaxCheckpoints)
            return; // At cap — keep walking the existing chain.

        // Only add a checkpoint if _lastClearSightPos is meaningfully different from the
        // current active checkpoint, preventing duplicate entries when the active raycaster
        // never acquired sight of the target.
        if (_lastClearSightPos.DistanceTo(activePos) > CheckpointReachDistance)
        {
            _checkpoints.Add(_lastClearSightPos);
            _activeRaycasterHadSight = false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void AbandonChain()
    {
        _checkpoints.Clear();
        _lostSightTimer = 0f;
        _state = NavState.Waiting;
    }

    private void RecalculateSteering()
    {
        if (_state == NavState.Waiting || _target == null)
        {
            _desiredVelocity = Vector2.Zero;
            return;
        }

        Vector2 toDestination = (_steeringDestination - GlobalPosition).Normalized();
        var spaceState = GetWorld2D().DirectSpaceState;

        for (int i = 0; i < RayCount; i++)
        {
            // Interest: how much this ray points toward the destination.
            _interest[i] = Mathf.Max(0f, _rayDirs[i].Dot(toDestination));

            // Danger: 1 if the ray hits any physics body the enemy collides with.
            var query = PhysicsRayQueryParameters2D.Create(
                GlobalPosition,
                GlobalPosition + _rayDirs[i] * RayLength,
                CollisionMask
            );
            query.Exclude = [GetRid()];
            _danger[i] = spaceState.IntersectRay(query).Count > 0 ? 1f : 0f;
        }

        Vector2 chosen = Vector2.Zero;
        for (int i = 0; i < RayCount; i++)
        {
            float weight = _interest[i] - _danger[i];
            if (weight > 0f)
                chosen += _rayDirs[i] * weight;
        }

        // Fallback: if every forward direction is blocked, push straight toward
        // the destination and let MoveAndSlide handle the slide.
        if (chosen == Vector2.Zero)
            chosen = toDestination;

        _desiredVelocity = chosen.Normalized() * MoveSpeed;
    }

    /// <summary>
    /// Returns true if there is an unobstructed line between <paramref name="from"/> and
    /// <paramref name="to"/>. Pass a positive <paramref name="range"/> to add a distance cap
    /// (used for sight checks). Pass a negative value (default) for uncapped geometry checks
    /// (used for path-integrity checks to waypoints).
    /// </summary>
    private bool HasLineOfSight(Vector2 from, Vector2 to, float range = -1f)
    {
        if (range > 0f && from.DistanceTo(to) > range)
            return false;

        var spaceState = GetWorld2D().DirectSpaceState;
        var query = PhysicsRayQueryParameters2D.Create(from, to, RaycastCollisionMask);
        query.Exclude = [GetRid()];

        var result = spaceState.IntersectRay(query);

        if (DebugDraw && result.Count > 0)
        {
            var hitPos = result["position"].AsVector2();
            string colliderName = result["collider"].As<GodotObject>() is Node n ? n.Name : "unknown";
            _dbgHits.Add((hitPos, colliderName));
        }

        return result.Count == 0;
    }

    // ── Debug drawing ──────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (!DebugDraw) return;

        switch (_state)
        {
            case NavState.Waiting:
                // Dim circle — enemy is idle, no LoS.
                DrawCircle(Vector2.Zero, 5f, Colors.DimGray);
                break;

            case NavState.Chasing:
                // Line from enemy to target: lime = sight clear, orange-red = debouncing/blocked.
                Color losColor = _dbgEnemyCanSeeTarget ? Colors.Lime : Colors.OrangeRed;
                DrawLine(Vector2.Zero, ToLocal(_dbgTargetPos), losColor, 1f);
                DrawCircle(Vector2.Zero, 4f, losColor);
                break;

            case NavState.FollowingChain:
                if (_checkpoints.Count == 0) break;

                // Yellow — enemy → next waypoint (path integrity check).
                DrawLine(Vector2.Zero, ToLocal(_checkpoints[0]), Colors.Yellow, 1f);
                DrawCircle(Vector2.Zero, 4f, Colors.Yellow);

                // Orange — inert chain segments between consecutive checkpoints.
                for (int i = 0; i < _checkpoints.Count - 1; i++)
                    DrawLine(ToLocal(_checkpoints[i]), ToLocal(_checkpoints[i + 1]), Colors.Orange, 1f);

                // White dots — inert checkpoint positions.
                for (int i = 0; i < _checkpoints.Count - 1; i++)
                    DrawCircle(ToLocal(_checkpoints[i]), 3f, Colors.White);

                // Cyan — active raycaster (last checkpoint) → current target position.
                Vector2 activeLocal = ToLocal(_checkpoints[_checkpoints.Count - 1]);
                DrawLine(activeLocal, ToLocal(_dbgTargetPos), Colors.Cyan, 1f);
                DrawCircle(activeLocal, 5f, Colors.Cyan);
                break;
        }

        // Magenta — last position the active raycaster saw the target clearly.
        // Visible in all states as a breadcrumb of where the target was last confirmed.
        DrawCircle(ToLocal(_lastClearSightPos), 4f, Colors.Magenta);

        // Red — where each blocked raycast actually hit geometry this frame.
        foreach (var (hitPos, _) in _dbgHits)
            DrawCircle(ToLocal(hitPos), 4f, Colors.Red);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetTarget(Node2D target)
    {
        _target = target;
    }

    public void ClearTarget()
    {
        _target = null;
        _checkpoints.Clear();
        _lostSightTimer = 0f;
        _state = NavState.Waiting;
        Velocity = Vector2.Zero;
    }

    public Node2D Target => _target;

    /// <summary>Read-only snapshot of the current checkpoint chain for debug drawing.</summary>
    public IReadOnlyList<Vector2> Checkpoints => _checkpoints;

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
