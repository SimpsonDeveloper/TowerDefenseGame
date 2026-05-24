using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Variant data for the shared pocket-dimension enemy. The enemy *scene* owns
/// the component structure (health/attacker/sprite/nav wiring); an EnemyType
/// supplies the per-variant numbers and the targeting strategy, which
/// <see cref="EnemyNavController.ApplyType"/> pushes into those components at
/// spawn time. This keeps one source of truth: structure in the scene, tuning
/// here.
/// </summary>
[GlobalClass]
public partial class EnemyType : Resource
{
    [Export] public string DisplayName { get; set; } = "Enemy";

    /// <summary>Sprite texture applied to the enemy's SpriteComponent.</summary>
    [Export] public Texture2D Sprite { get; set; }

    [Export] public int MaxHp { get; set; } = 100;
    [Export] public float MoveSpeed { get; set; } = 110f;

    [ExportGroup("Attack")]
    /// <summary>HP removed per attack tick (→ AttackerComponent.Damage).</summary>
    [Export] public int Damage { get; set; } = 10;
    /// <summary>Seconds between attack ticks (→ AttackerComponent.AttackInterval).</summary>
    [Export] public float AttackInterval { get; set; } = 0.5f;

    [ExportGroup("Targeting")]
    /// <summary>Which targeting strategy this variant uses. Builds the targeter
    /// node at spawn; null leaves the scene's default targeter in place.</summary>
    [Export] public TargetingPattern Targeting { get; set; }
}