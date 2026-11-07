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
/// <b>Turrets pull it off course when they are close.</b> Past <c>AggroRadius</c> the enemy walks
/// on by and its attacker chews through whatever it happens to brush; inside it, the turret
/// becomes the objective until it falls. A turret near the route is therefore a turret that gets
/// attacked, which is what makes placement a risk rather than a free choice.
///
/// <b>Walks the player's route when one is laid</b>, far end first, and is <b>railroaded</b> while
/// it does: an enemy following a route it believes in walks past the guns. Breaking the illusion is what frees it to turn on one
/// (<c>impl-planning/combat/paths.md</c>).
/// </summary>
[GlobalClass]
public partial class CoreTargetingPattern : TargetingPattern
{
    public override EnemyTargeter Build(EnemyType enemyType) => new EnemyStructureTargeter
    {
        TargetGroup = Core.CoreGroup,
        AggroGroup = "Towers",
        AggroRadius = enemyType.AggroRadius,
        AttackRange = enemyType.AttackRange,
        FollowsRoute = true,
    };
}
