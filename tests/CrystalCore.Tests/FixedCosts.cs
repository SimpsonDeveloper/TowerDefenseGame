using System.Collections.Generic;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.tests.crystal;

/// <summary>
/// Cost table for the abstract worked examples in energy-conservation.md (costs 1 / 2 / 3),
/// which do not use the shipping per-kind costs.
/// </summary>
public sealed class FixedCosts : ICostTable
{
    private readonly Dictionary<CrystalKind, double> _costs = new();

    public FixedCosts(params (CrystalKind Kind, double Cost)[] entries)
    {
        foreach (var (kind, cost) in entries) _costs[kind] = cost;
    }

    public double Cost(CrystalKind kind) => _costs.TryGetValue(kind, out var c) ? c : 0;
}
