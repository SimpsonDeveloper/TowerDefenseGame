using Godot;
using towerdefensegame.scripts.combat;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.world.enemies.targeting;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Coordinator for an enemy that walks the baked nav mesh. Owns the physical body and movement
/// loop; selection of what to walk toward lives in a child <see cref="EnemyStructureTargeter"/>.
///
/// Per physics tick:
///   1. Drives <see cref="EnemyStructureTargeter.Tick"/> so any pending pathfind
///      result is drained before movement consumes the destination.
///   2. If it has a path and has not reached its end, steers at the next point via
///      <c>MoveAndSlide</c>.
///
/// Wiring: subscribes to the targeter's events
/// (<see cref="EnemyStructureTargeter.ApproachResolved"/> → <see cref="SetDestination"/>,
/// <see cref="EnemyStructureTargeter.TargetCleared"/> → <see cref="Stop"/>).
///
/// <b>Paths come from <see cref="NavPaths.Find"/>, not a NavigationAgent2D.</b> The agent can
/// only path with Godot's raw query, which bends at the corners where nav regions meet, and it
/// offers no way to hand it a better path. The route the player lays is drawn from
/// <see cref="NavPaths"/>, and what is drawn must be what is walked, so the enemy follows the same
/// path itself. Following a polyline is a few lines; the agent was only ever doing that, plus
/// re-querying when the mesh changed, which <see cref="_mapIteration"/> does here.
/// </summary>
[GlobalClass]
public partial class EnemyNavController : CharacterBody2D, IMoveSpeed
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    /// <summary>
    /// Speed in px/s. Writable from outside (<see cref="IMoveSpeed"/>) so a slowing state can
    /// scale it.
    /// </summary>
    [Export] public float MoveSpeed { get; set; } = 160f;
    [Export] public float Acceleration { get; set; } = 10f;

    [ExportGroup("Navigation")]
    [Export] public CollisionShape2D Hitbox { get; set; }

    /// <summary>How close the enemy must get to each path point before it steers at the next.</summary>
    [Export] public float PathDesiredDistance { get; set; } = 16f;

    /// <summary>How close the enemy must get to the end of its path to count as arrived.</summary>
    [Export] public float TargetDesiredDistance { get; set; } = 2f;

    [ExportGroup("Components")]
    [Export] public HealthComponent Health { get; set; }

    /// <summary>The Mind meter, so a variant can set how hard it is to railroad.</summary>
    [Export] public MindComponent Mind { get; set; }
    [Export] public AttackerComponent Attacker { get; set; }
    [Export] SpriteComponent Sprite { get; set; }

    /// <summary>The targeting strategy, built and owned at spawn by
    /// <see cref="ApplyType"/>. Never set in the scene — null until a type with a
    /// <see cref="EnemyType.Targeting"/> is applied.</summary>
    public EnemyTargeter Targeter { get; private set; }

    // ── Virtual hooks ─────────────────────────────────────────────────────

    protected virtual void OnReady() { }

    // ── Internal state ────────────────────────────────────────────────────

    private bool _hasDestination;
    private Vector2 _destination;

    /// <summary>The path being walked, from <see cref="NavPaths.Find"/>. Read by the debug view.</summary>
    public Vector2[] Path { get; private set; } = System.Array.Empty<Vector2>();

    /// <summary>Index into <see cref="Path"/> of the point being steered at.</summary>
    public int PathIndex { get; private set; }

    /// <summary>
    /// The nav map's iteration when <see cref="Path"/> was found. A tower landing rebakes the mesh
    /// and bumps it; a path from an older mesh may run through the new tower, so it is re-found.
    /// </summary>
    private long _mapIteration = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public sealed override void _Ready()
    {
        if (Hitbox?.Shape is CircleShape2D circle)
            circle.Radius = GlobalEnemyConfig.AgentRadius;
        else
            GD.PushWarning($"{Name}: Hitbox missing or not a CircleShape2D — physical radius won't match nav radius.");

        AddToGroup("enemies");

        if (Targeter != null)
        {
            Targeter.ApproachResolved += SetDestination;
            Targeter.TargetCleared += Stop;
        }
        else
        {
            GD.PushWarning($"{Name}: no Targeter — EnemyType.Targeting was unset (or ApplyType not called); enemy will idle.");
        }

        // Default death behaviour: just leave the scene tree. Drop spawning
        // can hook the same signal later without changing this controller.
        if (Health != null)
            Health.Destroyed += QueueFree;

        OnReady();
    }

    public sealed override void _ExitTree()
    {
        if (Targeter != null)
        {
            Targeter.ApproachResolved -= SetDestination;
            Targeter.TargetCleared -= Stop;
        }
    }

    public sealed override void _PhysicsProcess(double delta)
    {
        QueueRedraw();

        // Drive the targeter first so a result that lands this tick is applied
        // before movement consumes it.
        Targeter?.Tick(delta);

        if (_hasDestination && MapIteration != _mapIteration)
            Repath();

        if (!_hasDestination || !TryNextPathPoint(out Vector2 nextPos))
        {
            Velocity = Vector2.Zero;
            return;
        }

        Vector2 desiredVelocity = (nextPos - GlobalPosition).Normalized() * MoveSpeed;
        Velocity = Velocity.Lerp(desiredVelocity, Acceleration * (float)delta);
        MoveAndSlide();

        if (Sprite != null)
        {
            if (Velocity.X > 0)      Sprite.FlipH = true;
            else if (Velocity.X < 0) Sprite.FlipH = false;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a variant's data to this enemy's components and builds its
    /// targeting strategy. This is the ONLY thing that creates a Targeter — the
    /// scene ships without one. Must be called after Instantiate but BEFORE the
    /// node enters the tree, so values land before the components' _Ready runs
    /// (HealthComponent latches MaxHp there) and the targeter is wired by this
    /// controller's _Ready. If Targeting is null the enemy idles (warned in _Ready).
    /// </summary>
    public void ApplyType(EnemyType type)
    {
        if (type == null) return;

        MoveSpeed = type.MoveSpeed;
        if (Health != null)
        {
            Health.MaxHp = type.MaxHp;
            if (Health.Shield != null) Health.Shield.MaxShield = type.MaxShield;
        }
        if (Mind != null) Mind.MaxMind = type.MaxMind;
        if (Sprite != null && type.Sprite != null) Sprite.Texture = type.Sprite;
        if (Attacker != null)
        {
            Attacker.Damage = type.Damage;
            Attacker.AttackInterval = type.AttackInterval;
        }

        if (type.Targeting != null)
        {
            Targeter = type.Targeting.Build(type);
            AddChild(Targeter);
        }
    }

    /// <summary>Current target tower, or null. Sourced from the targeter.</summary>
    public Node2D Target => Targeter?.CurrentTarget;

    /// <summary>
    /// Sets a navmesh destination and starts moving toward it. The targeter re-announces the same
    /// destination on every retarget, so an unchanged one keeps the path it has rather than paying
    /// for it again.
    /// </summary>
    public void SetDestination(Vector2 pos)
    {
        if (_hasDestination && pos.DistanceSquaredTo(_destination) < 1f) return;

        _hasDestination = true;
        _destination = pos;
        Repath();
    }

    /// <summary>Abandons the destination and the path with it.</summary>
    public void Stop()
    {
        _hasDestination = false;
        Velocity = Vector2.Zero;
        Path = System.Array.Empty<Vector2>();
        PathIndex = 0;
    }

    // ── Path following ────────────────────────────────────────────────────

    private Rid NavMap => GetWorld2D()?.NavigationMap ?? default;

    private long MapIteration
    {
        get
        {
            Rid map = NavMap;
            return map.IsValid ? (long)NavigationServer2D.MapGetIterationId(map) : -1;
        }
    }

    private void Repath()
    {
        _mapIteration = MapIteration;
        Path = NavPaths.Find(NavMap, GlobalPosition, _destination);
        PathIndex = 0;
    }

    /// <summary>
    /// The point to steer at, skipping any already within reach. False once the last point is
    /// reached, or when there is no path at all, which before the first bake is every path.
    /// </summary>
    private bool TryNextPathPoint(out Vector2 next)
    {
        next = default;
        if (Path.Length == 0) return false;

        while (PathIndex < Path.Length - 1
               && GlobalPosition.DistanceTo(Path[PathIndex]) <= PathDesiredDistance)
            PathIndex++;

        next = Path[PathIndex];
        if (PathIndex == Path.Length - 1 && GlobalPosition.DistanceTo(next) <= TargetDesiredDistance)
            return false;

        return true;
    }
}
