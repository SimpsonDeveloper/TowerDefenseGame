namespace towerdefensegame.scripts.combat.core;

/// <summary>
/// A plain <see cref="IEnemyVitals"/> holding fixed numbers — the default for an enemy nothing has
/// wired up, and what tests hand an <see cref="EnemyState"/> to pin a scenario.
///
/// In the running game the port is implemented by the Godot component that owns each value, not by
/// this: a record cannot report HP that moves.
/// </summary>
/// <param name="MaxHp">The enemy's full health pool.</param>
/// <param name="MaxMind">Illusion resistance at full.</param>
/// <param name="MoveSpeed">Unmodified speed in px/s.</param>
public sealed record EnemyVitals(double MaxHp = 100, double MaxMind = 100, double MoveSpeed = 0)
    : IEnemyVitals
{
    private readonly double? _hp;
    private readonly double? _mind;

    /// <summary>Full unless a test says otherwise: <c>new EnemyVitals(MaxHp: 200) { Hp = 50 }</c>.</summary>
    public double Hp
    {
        get => _hp ?? MaxHp;
        init => _hp = value;
    }

    /// <summary>Full unless a test says otherwise.</summary>
    public double Mind
    {
        get => _mind ?? MaxMind;
        init => _mind = value;
    }
}
