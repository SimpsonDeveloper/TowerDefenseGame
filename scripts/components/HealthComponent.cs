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

    [Signal] public delegate void DamagedEventHandler(double amount, double hp);
    [Signal] public delegate void DestroyedEventHandler();

    public double Hp { get; private set; }
    public bool IsDead => Hp <= 0;

    public override void _Ready()
    {
        Hp = MaxHp;
    }

    /// <summary>Apply damage. Non-positive amounts are ignored, and Destroyed
    /// fires at most once even if TakeDamage is called again post-mortem.</summary>
    public void TakeDamage(double amount)
    {
        if (amount <= 0 || IsDead) return;

        Hp = Mathf.Max(Hp - amount, 0);
        EmitSignal(SignalName.Damaged, amount, Hp);

        if (IsDead)
            EmitSignal(SignalName.Destroyed);
    }
}
