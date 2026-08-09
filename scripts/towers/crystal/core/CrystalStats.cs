namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// Energy a crystal draws from the stream as it passes (the local toll).
/// Injectable so tests can encode the abstract worked examples in
/// <c>docs/tower-design/energy-conservation.md</c> (costs 1 / 2 / 3).
/// </summary>
public interface ICostTable
{
    double Cost(CrystalKind kind);
}

/// <summary>Shipping costs — mirrors <c>CRYSTALS[*].cost</c> in <c>crystal-core.js</c>.</summary>
public sealed class CrystalStats : ICostTable
{
    public static readonly CrystalStats Default = new();

    public double Cost(CrystalKind kind) => kind switch
    {
        CrystalKind.Ruby => 28,
        CrystalKind.Sapphire => 16,
        CrystalKind.Emerald => 22,
        CrystalKind.Citrine => 12,
        CrystalKind.Amethyst => 20,
        CrystalKind.Quartz => 6,
        _ => 0,
    };
}
