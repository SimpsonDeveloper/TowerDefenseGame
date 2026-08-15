using System;
using System.Collections.Generic;
using System.Linq;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// Lattice → weapon-and-ops compiler. Engine-free (no <c>using Godot</c>): Godot and the UI call
/// in, it never calls back.
///
/// Three steps, each a pure function of its arguments:
///   1. <see cref="RouteEnergy"/> — lattice + seeded share → energy at every crystal
///   2. <see cref="NameOps"/>     — lattice + energy → one op per internal edge
///   3. <see cref="OrderShot"/>   — ops → the ordered shot (STUBBED, roadmap item 2)
///
/// There is no terminal pass and no reachability pass. Source and sink are *predicates on the
/// lattice* (<see cref="Lattice.IsSource"/> / <see cref="Lattice.IsSink"/>), and because every
/// crystal is therefore reachable from a source and reaches a sink, "fed" and "productive" are
/// always true and are not modelled. They become real again only if a future rule can switch an
/// edge off — see the note on <see cref="NameOps"/>.
/// </summary>
public static class Compiler
{
    public static CompileResult Compile(Lattice lattice, double coreEnergy, ICostTable costs = null)
    {
        costs ??= CrystalStats.Default;

        IReadOnlyList<Cell> sourceCells = lattice.Sources();
        IReadOnlyList<Cell> sinkCells = lattice.Sinks();

        // The core is split EQUALLY among the sources — no weights.
        double share = sourceCells.Count > 0 ? coreEnergy / sourceCells.Count : 0;

        IReadOnlyDictionary<int, CellEnergy> energy = RouteEnergy(lattice, share, costs);
        IReadOnlyList<EdgeOp> ops = NameOps(lattice, energy);

        List<Terminal> sources = sourceCells
            .Select((Cell cell, int i) => new Terminal(cell, $"S{i + 1}", share))
            .ToList();
        List<Terminal> sinks = sinkCells
            .Select((Cell cell, int i) => new Terminal(cell, $"T{i + 1}", Math.Max(0, energy[cell.Id].Out)))
            .ToList();

        return new CompileResult
        {
            CoreEnergy = coreEnergy,
            UsedCost = lattice.Cells.Sum(cell => costs.Cost(cell.Kind)),
            WeaponEnergy = sinks.Sum(sink => sink.Energy),
            Sources = sources,
            Sinks = sinks,
            Ops = ops,
            Energy = energy,
            Shot = OrderShot(ops),
        };
    }

    /// <summary>
    /// One bottom→top sweep. Each crystal takes what its in-neighbours hand up, draws its own
    /// cost (the local toll), and offers the remainder to its out-neighbours — a ▲ split between
    /// two, a ▽ all to one. Nothing is clamped in transit, so a branch can run into debt and a
    /// ▽ downstream can sum it back above 0.
    /// </summary>
    /// <param name="share">What each source is seeded, i.e. <c>E_core / sourceCount</c>.</param>
    private static IReadOnlyDictionary<int, CellEnergy> RouteEnergy(
        Lattice lattice, double share, ICostTable costs)
    {
        Dictionary<int, CellEnergy> energy = new(lattice.Cells.Count);

        foreach (Cell cell in lattice.FlowOrder())   // guarantees in-neighbours are already done
        {
            double inflow = lattice.IsSource(cell)
                ? share                              // seeded by the core; nothing feeds a source
                : lattice.InNeighbors(cell).Sum(up => energy[up.Id].PerOut);
            //   ▽ sums its two inputs; a ▲ has exactly one, so the same sum covers both.

            double cost = costs.Cost(cell.Kind);
            energy[cell.Id] = new CellEnergy(
                In: inflow,
                Cost: cost,
                Out: inflow - cost,
                OutCount: lattice.OutNeighbors(cell).Count);
        }

        return energy;
    }

    /// <summary>
    /// Every internal edge fires the combo its two crystals name in the matrix, at the energy
    /// that crossed it — the upstream crystal's per-output share, floored at 0. Debt is recorded
    /// but produces nothing.
    ///
    /// Every internal edge fires, because every one of them lies on a source→sink path. If
    /// conditional branching later lets a split switch an edge off, that filter belongs here:
    /// the sweep above already divides only among *live* out-neighbours, so a disabled branch
    /// simply hands its share to its sibling.
    /// </summary>
    private static IReadOnlyList<EdgeOp> NameOps(
        Lattice lattice, IReadOnlyDictionary<int, CellEnergy> energy)
    {
        List<EdgeOp> ops = new();

        foreach (Cell down in lattice.FlowOrder())
        foreach (Cell up in lattice.InNeighbors(down))
        {
            double crossing = energy[up.Id].PerOut;
            ops.Add(new EdgeOp(
                Up: up,
                Down: down,
                Op: ComboMatrix.ComboOp(up.Kind, down.Kind),
                Energy: Math.Max(0, crossing),
                Debt: crossing < 0));
        }

        return ops;
    }

    /// <summary>
    /// STUBBED — roadmap item 2 (`op-flow.md`). Will flatten the ops into ONE ordered list (no
    /// bag, no compile-time consume, no split/merge of quantities), sorted by the producing
    /// crystal's <see cref="CellCoord.Height"/> ascending, then its column. Returns empty for
    /// now so callers can already read <see cref="CompileResult.Shot"/>.
    /// </summary>
    private static IReadOnlyList<ShotOp> OrderShot(IReadOnlyList<EdgeOp> ops) => new List<ShotOp>();
}
