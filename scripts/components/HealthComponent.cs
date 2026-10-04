using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// Reusable HP/damage component. Attach as a child of any node that can be
/// damaged (towers, enemies). Owners subscribe to <see cref="Destroyed"/> to
/// react when HP hits zero; the component itself never frees the owner so
/// each owner can run its own teardown (towers fan out
/// <c>ITowerPlaceable.Destroyed</c> for footprint release, enemies may drop
/// resources, etc.).
///
/// HP is a <c>double</c> because damage-over-time deals fractions of a point: a burn ticking
/// half a point at a time has to actually land, not round to nothing. Whole-number damage still
/// widens for free, so a gun hitting for 10 reads exactly as it did.
/// </summary>
[GlobalClass]
public partial class HealthComponent : Node
{
    [Export] public double MaxHp { get; set; } = 10;

    /// <summary>
    /// Optional layer in front of HP. Damage routes through it first; leave it null and nothing
    /// about this component changes, which is how towers and unshielded enemies stay unaffected.
    /// </summary>
    [Export] public ShieldComponent Shield { get; set; }

    /// <summary>Damage specifically. For the flash, hit feedback, anything that cares that it HURT.</summary>
    [Signal] public delegate void DamagedEventHandler(double amount, double hp);

    /// <summary>
    /// HP moved, for any reason. What a bar should listen to: a future <c>Heal</c> emits this and
    /// the bar is correct without being told about healing.
    /// </summary>
    [Signal] public delegate void ChangedEventHandler(double hp);

    [Signal] public delegate void DestroyedEventHandler();

    /// <summary>
    /// Null until something moves it, which reads as full. <see cref="MaxHp"/> is applied after
    /// construction (an enemy's type is applied before it enters the tree), so a stored Hp would
    /// have to be filled in <c>_Ready</c> — and anything reading it from its own <c>_Ready</c>
    /// would race that. Starting full by default removes the race without making HP one-way.
    /// </summary>
    private double? _hp;

    public double Hp => _hp ?? MaxHp;

    public bool IsDead => Hp <= 0;

    /// <summary>
    /// Apply damage, through the shield if there is one. Non-positive amounts are ignored, and
    /// Destroyed fires at most once even if TakeDamage is called again post-mortem.
    ///
    /// <see cref="Damaged"/> carries the <b>whole</b> hit and fires even when the shield ate all of
    /// it — it did hurt, and hit feedback should say so. <see cref="Changed"/> only fires when HP
    /// actually moved, because that is what a bar cares about.
    /// </summary>
    public void TakeDamage(double amount)
    {
        if (amount <= 0 || IsDead) return;

        double toHp = Shield != null ? Shield.Absorb(amount) : amount;

        EmitSignal(SignalName.Damaged, amount, Hp);
        if (toHp <= 0) return;

        _hp = Mathf.Max(Hp - toHp, 0);
        EmitSignal(SignalName.Changed, Hp);

        if (IsDead)
            EmitSignal(SignalName.Destroyed);
    }
}
