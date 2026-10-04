namespace towerdefensegame.scripts.components;

/// <summary>
/// Anything whose movement speed can be written from outside — the surface a slowing state needs.
///
/// Two unrelated controllers already carry a <c>MoveSpeed</c> export and neither knows about
/// combat; this names that shared property so a state can reach it without knowing which one it
/// has. Nothing else is promised: how the node turns speed into motion is entirely its own.
///
/// The <b>base</b> speed is not here. Whoever scales this is expected to have captured the
/// unmodified value first (combat keeps it in <c>EnemyVitals.MoveSpeed</c>), because writing a
/// scaled value back over the source would compound every frame.
/// </summary>
public interface IMoveSpeed
{
    float MoveSpeed { get; set; }
}
