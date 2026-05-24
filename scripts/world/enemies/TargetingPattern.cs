using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Data-driven factory for an <see cref="EnemyTargeter"/>. Each concrete pattern
/// is a Resource subclass that carries its own tuning and builds the matching
/// targeter node at spawn time. <see cref="EnemyType"/> holds one of these, so a
/// variant picks its targeting strategy purely as data — no class-name lookup,
/// no hardcoded config — and the controller never learns which concrete type it
/// received.
/// </summary>
public abstract partial class TargetingPattern : Resource
{
    /// <summary>
    /// Builds a configured targeter node. <paramref name="enemyConfig"/> is the
    /// owner's shared global config (agent radius, etc.); patterns that need it
    /// use it, the rest ignore it. The caller adds the node to the enemy and
    /// subscribes to its events.
    /// </summary>
    public abstract EnemyTargeter Build(EnemyConfig enemyConfig);
}