using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Coordinator for an enemy that delegates pathfinding to Godot's built-in
/// NavigationAgent2D. Owns the physical body and movement loop; selection of
/// what to walk toward lives in a child <see cref="EnemyTowerTargeter"/>.
///
/// Per physics tick:
///   1. Drives <see cref="EnemyTowerTargeter.Tick"/> so any pending pathfind
///      result is drained before movement consumes the destination.
///   2. If the NavAgent has a destination and isn't finished, follows the next
///      path waypoint via <c>MoveAndSlide</c>.
///
/// Wiring: subscribes to the targeter's events and forwards them to NavAgent
/// (<see cref="EnemyTowerTargeter.ApproachResolved"/> → <see cref="SetDestination"/>,
/// <see cref="EnemyTowerTargeter.TargetCleared"/> → <see cref="Stop"/>).
///
/// Note: NavAgent re-queries the navmesh internally when TargetPosition is set
/// (in addition to the targeter's MapGetPath check). Accepted duplicate in
/// exchange for NavAgent doing path-follow and smoothing for us.
/// </summary>
[GlobalClass]
public partial class EnemyNavController : CharacterBody2D
{
    // ── Configuration ─────────────────────────────────────────────────────

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 80f;
    [Export] public float Acceleration { get; set; } = 10f;

    [ExportGroup("Navigation")]
    [Export] public NavigationAgent2D NavAgent;
    [Export] public CollisionShape2D Hitbox { get; set; }
    [Export] public EnemyConfig EnemyConfig { get; set; }

    /// <summary>
    /// How close the agent must get to each path waypoint before it advances.
    /// Keep this at roughly half a tile width (4px for 8px tiles).
    /// </summary>
    [Export] public float PathDesiredDistance { get; set; } = 8f;

    /// <summary>
    /// How close the agent must get to the final target before navigation is
    /// considered finished.
    /// </summary>
    [Export] public float TargetDesiredDistance { get; set; } = 1f;

    [ExportGroup("Components")]
    [Export] public HealthComponent Health { get; set; }
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

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public sealed override void _Ready()
    {
        NavAgent.PathDesiredDistance = PathDesiredDistance;
        NavAgent.TargetDesiredDistance = TargetDesiredDistance;
        NavAgent.MaxSpeed = MoveSpeed;
        if (EnemyConfig != null)
        {
            NavAgent.Radius = EnemyConfig.AgentRadius;
            if (Hitbox?.Shape is CircleShape2D circle)
                circle.Radius = EnemyConfig.AgentRadius;
            else
                GD.PushWarning($"{Name}: Hitbox missing or not a CircleShape2D — physical radius won't match nav radius.");
        }
        else
        {
            GD.PushWarning($"{Name}: EnemyConfig not assigned — NavAgent.Radius and hitbox may not match nav-bake radius.");
        }

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

        if (!_hasDestination || NavAgent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            return;
        }

        Vector2 nextPos = NavAgent.GetNextPathPosition();
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
        if (Health != null) Health.MaxHp = type.MaxHp;
        if (Sprite != null && type.Sprite != null) Sprite.Texture = type.Sprite;
        if (Attacker != null)
        {
            Attacker.Damage = type.Damage;
            Attacker.AttackInterval = type.AttackInterval;
        }

        if (type.Targeting != null)
        {
            Targeter = type.Targeting.Build(EnemyConfig);
            AddChild(Targeter);
        }
    }

    /// <summary>Current target tower, or null. Sourced from the targeter.</summary>
    public Node2D Target => Targeter?.CurrentTarget;

    /// <summary>Sets a navmesh destination and starts moving toward it.</summary>
    public void SetDestination(Vector2 pos)
    {
        _hasDestination = true;
        if (NavAgent != null)
            NavAgent.TargetPosition = pos;
    }

    /// <summary>Parks on the current position so any cached path is abandoned
    /// (IsNavigationFinished() goes true on the next query).</summary>
    public void Stop()
    {
        _hasDestination = false;
        Velocity = Vector2.Zero;
        if (NavAgent != null)
            NavAgent.TargetPosition = GlobalPosition;
    }
}
