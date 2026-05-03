using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// Reusable HP/damage component. Attach as a child of any node that can be
/// damaged (towers, enemies). Owners subscribe to <see cref="Destroyed"/> to
/// react when HP hits zero; the component itself never frees the owner so
/// each owner can run its own teardown (towers fan out
/// <c>ITowerPlaceable.Destroyed</c> for footprint release, enemies may drop
/// resources, etc.).
/// </summary>
[GlobalClass]
public partial class HealthComponent : Node
{
    [Export] public int MaxHp { get; set; } = 10;

    [Signal] public delegate void DamagedEventHandler(int amount, int hp);
    [Signal] public delegate void DestroyedEventHandler();

    public int Hp { get; private set; }
    public bool IsDead => Hp <= 0;

    public override void _Ready()
    {
        Hp = MaxHp;
    }

    /// <summary>Apply damage. Non-positive amounts are ignored, and Destroyed
    /// fires at most once even if TakeDamage is called again post-mortem.</summary>
    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead) return;

        Hp = Mathf.Max(Hp - amount, 0);
        EmitSignal(SignalName.Damaged, amount, Hp);

        if (Hp == 0)
            EmitSignal(SignalName.Destroyed);
    }
}
