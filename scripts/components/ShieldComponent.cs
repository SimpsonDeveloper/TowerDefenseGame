using Godot;

namespace towerdefensegame.scripts.components;

/// <summary>
/// A defensive layer in front of HP — a second bar that must be spent before HP takes anything
/// (<c>docs/tower-design/effect-vocab/vocab-overview/shield.md</c>). Attach beside a
/// <see cref="HealthComponent"/> and point that component's <c>Shield</c> at it; damage then routes
/// through here on its way in.
///
/// Each unit sits somewhere on a <b>shield : HP spectrum</b>, from none at all up to shield-heavy,
/// but never pure shield. <see cref="MaxShield"/> of 0 means no shield, and that is the default:
/// towers and unshielded enemies are unaffected by any of this.
///
/// <b>Down two ways.</b> Chipped to zero by damage — the shield's worth of HP is real damage
/// somebody had to pay — or <see cref="Disable"/>d outright by Scramble, which skips the bill and
/// leaves the points untouched. Both read as <see cref="IsDown"/>, and while down, damage passes
/// straight through.
///
/// Shield does <b>not</b> protect Mind: mind-damage goes to its own meter and never comes through
/// here.
/// </summary>
[GlobalClass]
public partial class ShieldComponent : Node
{
    /// <summary>Shield pool. Zero means this unit has no shield at all.</summary>
    [Export] public double MaxShield { get; set; }

    /// <summary>Shield moved. What a bar listens to.</summary>
    [Signal] public delegate void ChangedEventHandler(double shield);

    /// <summary>The shield stopped protecting — emptied or disabled. May fire more than once.</summary>
    [Signal] public delegate void DownEventHandler();

    private double? _shield;
    private double _disabledFor;
    private bool _wasDown;

    public double Shield => _shield ?? MaxShield;

    /// <summary>Does this unit have a shield at all? An unshielded one is not "down", it is absent.</summary>
    public bool HasShield => MaxShield > 0;

    /// <summary>Carrying a shield that is currently not protecting — spent, or scrambled.</summary>
    public bool IsDown => HasShield && (Shield <= 0 || _disabledFor > 0);

    /// <summary>
    /// Take what the shield can off an incoming hit and hand back the rest. The whole of it while
    /// the shield is down or absent, so callers can route unconditionally.
    /// </summary>
    public double Absorb(double damage)
    {
        if (damage <= 0 || !HasShield || IsDown) return damage;

        double absorbed = Mathf.Min(Shield, damage);
        _shield = Shield - absorbed;

        EmitSignal(SignalName.Changed, Shield);
        RaiseDownIfNewlyDown();

        return damage - absorbed;
    }

    /// <summary>
    /// Switch the shield off for a time without spending it — Scramble's EMP. On an unshielded
    /// unit there is nothing to disable and the effect is wasted, which is the bet Scramble makes
    /// on the wave.
    /// </summary>
    public void Disable(double seconds)
    {
        if (seconds <= 0 || !HasShield) return;

        // Longer wins, like any timed flat state (merge.md).
        if (seconds > _disabledFor) _disabledFor = seconds;

        EmitSignal(SignalName.Changed, Shield);
        RaiseDownIfNewlyDown();
    }

    public override void _Process(double delta)
    {
        if (_disabledFor <= 0) return;

        _disabledFor -= delta;
        if (_disabledFor > 0) return;

        _disabledFor = 0;
        EmitSignal(SignalName.Changed, Shield);   // it may be protecting again
        _wasDown = IsDown;
    }

    private void RaiseDownIfNewlyDown()
    {
        if (!IsDown)
        {
            _wasDown = false;
            return;
        }

        if (_wasDown) return;

        _wasDown = true;
        EmitSignal(SignalName.Down);
    }
}
