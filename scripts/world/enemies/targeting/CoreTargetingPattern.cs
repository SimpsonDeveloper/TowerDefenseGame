using Godot;
using towerdefensegame.scripts.world.structures;

namespace towerdefensegame.scripts.world.enemies.targeting;

/// <summary>
/// Targeting pattern that walks the enemy toward the map's <see cref="Core"/>, letting its
/// attacker deal with whatever structures it meets on the way. The default for an enemy variant:
/// it is the behaviour that needs nothing placed, so an enemy always has somewhere to be.
///
/// Builds the same <see cref="EnemyStructureTargeter"/> the tower pattern does, pointed at the
/// <c>Core</c> group instead. That is the whole difference, and it matters: the approach resolver
/// behind it stands the enemy off from the target's <b>footprint</b> rather than aiming at its
/// centre, which is the thing that cannot be re-derived cheaply and must not be re-written.
///
/// <b>Towers are handled by not handling them.</b> An enemy's <c>AttackerComponent</c> already
/// scans its own hitbox every physics frame and damages whatever structure is in it, with no help
/// from a targeter. A tower gets chewed through because the enemy is standing next to it, not
/// because anything chose it — which is the behaviour a maze wants.
/// </summary>
[GlobalClass]
public partial class CoreTargetingPattern : TargetingPattern
{
    public override EnemyTargeter Build(EnemyType enemyType) => new EnemyStructureTargeter
    {
        TargetGroup = Core.CoreGroup,
        AttackRange = enemyType.AttackRange,
    };
}
