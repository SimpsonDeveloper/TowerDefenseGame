using Godot;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// The enemy's <b>Mind</b> meter — illusion resistance, the second bar
/// (<c>effect-vocab/vocab-overview/illusion.md</c>). Attach beside a <c>HealthComponent</c>; the
/// two are the same shape on purpose.
///
/// <b>Only ever drained.</b> Mind-damage takes it down and nothing puts it back: the break is
/// permanent by design, and at zero the enemy can never deviate from the player's maze again. That
/// is the one way it differs from HP, which is a two-way resource.
///
/// Mind reads full until something drains it, so it is correct before any <c>_Ready</c> runs —
/// <see cref="MaxMind"/> is applied after construction, and a sibling reading the meter from its
/// own <c>_Ready</c> would otherwise race whoever filled it.
/// </summary>
[GlobalClass]
public partial class MindComponent : Node
{
    [Export] public double MaxMind { get; set; } = 100;

    /// <summary>Mind moved. What a bar listens to.</summary>
    [Signal] public delegate void ChangedEventHandler(double mind);

    /// <summary>
    /// Hit zero. Fires once: the break is permanent, so there is no second time. Roadmap item 5
    /// gives it meaning — a broken enemy stops rolling for deviation.
    /// </summary>
    [Signal] public delegate void BrokenEventHandler();

    private double? _mind;

    public double Mind => _mind ?? MaxMind;

    public bool IsBroken => Mind <= 0;

    /// <summary>Drain the meter. Non-positive amounts are ignored, and Broken fires at most once.</summary>
    public void Drain(double amount)
    {
        if (amount <= 0 || IsBroken) return;

        _mind = Mathf.Max(Mind - amount, 0);
        EmitSignal(SignalName.Changed, Mind);

        if (IsBroken)
            EmitSignal(SignalName.Broken);
    }
}
