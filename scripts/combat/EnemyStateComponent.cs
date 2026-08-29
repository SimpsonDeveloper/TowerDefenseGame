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
public partial class EnemyStateComponent : Node
{
    /// <summary>Where damage-over-time lands. Without it, states still run but nothing dies.</summary>
    [Export] public HealthComponent Health;

    /// <summary>Illusion resistance, the second bar. Meaningful once roadmap item 5 lands.</summary>
    [Export] public float MaxR = 100f;

    public EnemyState State { get; private set; }

    /// <summary>Everything states have dealt over this enemy's life. Readouts only.</summary>
    public double DotDamageDealt { get; private set; }

    public override void _Ready()
    {
        // Read once here rather than watched: an enemy's type is applied before it enters the
        // tree, precisely so component _Ready sees the final numbers
        // (EnemyNavController.ApplyType). Anything that later changes max HP reassigns
        // State.Vitals.
        State = new EnemyState(new EnemyVitals(Health?.MaxHp ?? 100, MaxR));

        if (Health == null)
            GD.PushWarning($"[combat] {GetParent()?.Name} has states but no HealthComponent — damage-over-time will go nowhere");
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

        // Pulled rather than pushed: EnemyState is engine-free and cannot reach a
        // HealthComponent, so it queues what it dealt and this drains it once a frame.
        double damage = State.TakeHpDamage();
        if (damage <= 0) return;

        DotDamageDealt += damage;
        Health?.TakeDamage(damage);
    }
}
