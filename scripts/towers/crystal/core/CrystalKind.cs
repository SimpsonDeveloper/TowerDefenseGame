namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// The crystal roster. Mirrors <c>CRYSTALS</c> in
/// <c>docs/tower-design/playground/archive/crystal-core.js</c>.
/// Element (Fire / Ice / ...) is flavor only and carries no mechanics, so it does not
/// live in the engine-free core.
/// </summary>
public enum CrystalKind
{
    Ruby,
    Sapphire,
    Emerald,
    Citrine,
    Amethyst,
    Quartz,
}
