using Godot;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// An enemy's carried states, and the bridge between them and the scene tree. Attach as a child
/// of anything a compiled shot can land on, beside its <see cref="HealthComponent"/>.
///
/// The rules themselves live in <see cref="EnemyState"/>, which is engine-free. This node owns
/// only what needs an engine: the clock (<c>_Process</c>) and the hand-off of accrued damage to
/// the HP component. Keeping the split here is what lets every op be tested without a scene.
/// </summary>
[GlobalClass]
public partial class EnemyStateComponent : Node, IEnemyVitals
{
    /// <summary>Where damage-over-time lands. Without it, states still run but nothing dies.</summary>
    [Export] public HealthComponent Health;

    /// <summary>Where mind-damage lands. Without it, Am–Am shots resolve and drain nothing.</summary>
    [Export] public MindComponent Mind;

    /// <summary>
    /// Built on construction, not in <c>_Ready</c>, so a sibling can subscribe to it in its own
    /// <c>_Ready</c> whatever order the two run in — the same guarantee a <c>HealthComponent</c>
    /// gives its bar by being a node. Its <see cref="EnemyState.Vitals"/> arrive later.
    /// </summary>
    public EnemyState State { get; } = new EnemyState();

    /// <summary>The owner, if it can be slowed. Null is fine — states just will not move it.</summary>
    private IMoveSpeed _movement;

    /// <summary>
    /// Speed before anything slowed it, captured once. Read back off the owner every frame and a
    /// slow would compound against its own output.
    /// </summary>
    private float _baseMoveSpeed;

    // ---- IEnemyVitals: the port ops read the enemy through ------------------------------------

    double IEnemyVitals.Hp => Health?.Hp ?? 0;

    double IEnemyVitals.MaxHp => Health?.MaxHp ?? 0;

    double IEnemyVitals.Mind => Mind?.Mind ?? 0;

    double IEnemyVitals.MaxMind => Mind?.MaxMind ?? 0;

    double IEnemyVitals.MoveSpeed => _baseMoveSpeed;

    /// <summary>Everything states have dealt over this enemy's life. Readouts only.</summary>
    public double DotDamageDealt { get; private set; }

    public override void _Ready()
    {
        // Read once here rather than watched: an enemy's type is applied before it enters the
        // tree, precisely so component _Ready sees the final numbers
        // (EnemyNavController.ApplyType). Anything that later changes max HP reassigns
        // State.Vitals.
        _movement = GetParent() as IMoveSpeed;
        _baseMoveSpeed = _movement?.MoveSpeed ?? 0f;

        // The component IS the port: ops read through it and get live values, rather than a
        // snapshot taken here that would be wrong the moment the enemy is shot.
        State.Vitals = this;

        if (Health == null)
            GD.PushWarning($"[combat] {GetParent()?.Name} has states but no HealthComponent — damage-over-time will go nowhere");

        if (Mind == null)
            GD.PushWarning($"[combat] {GetParent()?.Name} has states but no MindComponent — mind-damage will go nowhere");
    }

    /// <summary>
    /// Spend a shot on this enemy. The whole ordered list resolves in this call
    /// (<c>vocab-overview/states.md</c> → *Shot resolution*).
    /// </summary>
    public void Receive(CompileResult shot)
    {
        if (shot != null) ShotResolver.Resolve(shot.Shot, State);
    }

    public override void _Process(double delta)
    {
        State.Tick(delta, CombatRules.Default);

        ApplyMovement();

        // Pulled rather than pushed: EnemyState is engine-free and cannot reach a
        // HealthComponent, so it queues what it dealt and this drains it once a frame.
        Mind?.Drain(State.TakeMindDamage());

        double damage = State.TakeHpDamage();
        if (damage <= 0) return;

        DotDamageDealt += damage;
        Health?.TakeDamage(damage);
    }

    /// <summary>
    /// Rewrite the owner's speed from its captured base. Always from the base, never from the
    /// current value: scaling what is already scaled compounds every frame and an enemy that was
    /// chilled once would crawl forever.
    /// </summary>
    private void ApplyMovement()
    {
        if (_movement == null) return;

        _movement.MoveSpeed = (float)(_baseMoveSpeed * State.SpeedScale(CombatRules.Default));
    }
}
