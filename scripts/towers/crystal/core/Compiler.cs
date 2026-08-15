using System;
using System.Collections.Generic;
using System.Linq;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// Lattice → weapon-and-ops compiler. Engine-free (no <c>using Godot</c>): Godot and the UI
/// call in, it never calls back. Direct port of <c>compile()</c> in
/// <c>docs/tower-design/playground/archive/dataflow-playground.html</c>.
///
/// Passes (compiler-core.md §3): terminals → productivity → fed → energy routing → op
/// production → ordered shot (STUBBED, roadmap item 2) → collect.
/// </summary>
public static class Compiler
{
    public static CompileResult Compile(Lattice lattice, double coreEnergy, ICostTable costs = null)
    {
        costs ??= CrystalStats.Default;
        IReadOnlyList<Cell> order = lattice.FlowOrder();          // bottom→top

        double used = lattice.Cells.Sum(c => costs.Cost(c.Kind));

        // ---- pass 1: terminals (AUTO) --------------------------------------------------
        // source iff leaf on the input side, sink iff leaf on the output side; a lone crystal
        // is both. Derived every compile, always on, never user-set. The leaf-input /
        // leaf-output rules therefore hold by construction — no legality check needed.
        HashSet<int> isSource = new();
        HashSet<int> isSink = new();
        foreach (Cell cell in lattice.Cells)
        {
            if (!lattice.InNeighbors(cell).Any() && lattice.HasOpenIn(cell)) isSource.Add(cell.Id);
            if (!lattice.OutNeighbors(cell).Any() && lattice.HasOpenOut(cell)) isSink.Add(cell.Id);
        }

        // ---- pass 2: productivity (top→bottom) -----------------------------------------
        // a crystal reaches a sink if it IS one or one of its out-edges leads to one.
        Dictionary<int, bool> nodeProductive = new();
        Dictionary<(int, int), bool> edgeProductive = new();
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Cell cell = order[i];
            bool productive = isSink.Contains(cell.Id);
            foreach (Cell nb in lattice.OutNeighbors(cell))
            {
                bool edgeReachesSink = nodeProductive.TryGetValue(nb.Id, out bool nbProductive) && nbProductive;
                edgeProductive[Lattice.EdgeKey(cell, nb)] = edgeReachesSink;
                if (edgeReachesSink) productive = true;
            }

            nodeProductive[cell.Id] = productive;
        }

        // ---- seed: E_core split EQUALLY among the sources (no weights) -----------------
        List<Cell> sourceCells = lattice.Cells
            .Where(c => isSource.Contains(c.Id))
            .OrderBy(c => c.Col).ThenBy(c => c.Row)
            .ToList();
        double share = sourceCells.Count > 0 ? coreEnergy / sourceCells.Count : 0.0;

        Dictionary<int, double> seeded = sourceCells.ToDictionary(c => c.Id, _ => share);

        // ---- pass 3+4: fed + energy routing (bottom→top) -------------------------------
        // each crystal tolls its own cost; ▲ divides the remainder among its productive
        // outputs, ▽ sums its inputs. Debt (negative) flows unclamped so a later ▽ recovers it.
        Dictionary<(int, int), double> energyByEdge = new();
        Dictionary<int, NodeTrace> nodes = new();

        foreach (Cell cell in order)
        {
            bool src = isSource.Contains(cell.Id);
            bool sink = isSink.Contains(cell.Id);
            double cost = costs.Cost(cell.Kind);

            List<(int, int)> inEdges = lattice.InNeighbors(cell)
                .Select(nb => Lattice.EdgeKey(cell, nb))
                .Where(energyByEdge.ContainsKey)
                .ToList();
            List<(int, int)> outs = lattice.OutNeighbors(cell)
                .Select(nb => Lattice.EdgeKey(cell, nb))
                .Where(k => edgeProductive.TryGetValue(k, out bool p) && p)
                .ToList();

            double inSum;
            bool fed;
            if (src)
            {
                fed = seeded.TryGetValue(cell.Id, out inSum);    // seeded by the core
                if (!fed) inSum = 0;
            }
            else if (inEdges.Count == 0)
            {
                nodes[cell.Id] = Trace(cell, cost, 0, 0, 0, outs.Count, src, sink,
                    fed: false, productive: nodeProductive[cell.Id]);
                continue;                                        // unfed interior node → inert
            }
            else
            {
                fed = true;
                inSum = cell.IsUp
                    ? energyByEdge[inEdges[0]]                   // ▲ has exactly one input
                    : inEdges.Sum(k => energyByEdge[k]);         // ▽ sums (debts included)
            }

            double outE = inSum - cost;                          // local toll (may go negative)
            int nOut = outs.Count;
            double perOut = nOut > 1 ? outE / nOut : outE;
            if (!sink)                                           // sinks drain, they don't route on
                foreach ((int, int) k in outs) energyByEdge[k] = perOut;

            nodes[cell.Id] = Trace(cell, cost, inSum, outE, perOut, nOut, src, sink,
                fed, nodeProductive[cell.Id]);
        }

        // ---- pass 5: op production on active edges -------------------------------------
        // an edge carries energy only if it is fed AND productive — i.e. active.
        List<EdgeOp> edgeOps = new();
        foreach (Cell cell in order)
        {
            foreach (Cell nb in lattice.InNeighbors(cell))
            {
                (int, int) k = Lattice.EdgeKey(cell, nb);
                if (!energyByEdge.TryGetValue(k, out double energy)) continue;
                edgeOps.Add(new EdgeOp
                {
                    UpCellId = nb.Id,
                    DownCellId = cell.Id,
                    UpKind = nb.Kind,
                    DownKind = cell.Kind,
                    Op = ComboMatrix.ComboOp(nb.Kind, cell.Kind),
                    Energy = Math.Max(0, energy),
                    Debt = energy < 0,
                    DownFlowDepth = cell.FlowDepth,
                    DownCol = cell.Col,
                    UpCol = nb.Col,
                });
            }
        }

        // ---- pass 6: ordered shot — STUBBED (roadmap item 2, op-flow.md) ---------------
        // Flattens the active EdgeOps into ONE ordered list (no bag, no compile-time consume,
        // no split/merge of quantities), sorted by lattice position. Left empty here; the seam
        // exists so callers can already read CompileResult.Shot.
        List<ShotOp> shot = new();

        // ---- pass 7: collect -----------------------------------------------------------
        List<Cell> sinkCells = lattice.Cells
            .Where(c => isSink.Contains(c.Id))
            .OrderBy(c => c.Col).ThenBy(c => c.Row)
            .ToList();
        Dictionary<int, double> sinkEnergy = sinkCells.ToDictionary(
            c => c.Id,
            c => Math.Max(0, nodes.TryGetValue(c.Id, out NodeTrace n) ? n.OutE : 0));
        double weaponEnergy = sinkEnergy.Values.Sum();

        // ---- terminal labels (S# / T#) -------------------------------------------------
        Dictionary<int, string> sourceLabels = sourceCells
            .Select((Cell c, int i) => (c.Id, Label: $"S{i + 1}"))
            .ToDictionary(t => t.Id, t => t.Label);
        Dictionary<int, string> sinkLabels = sinkCells
            .Select((Cell c, int i) => (c.Id, Label: $"T{i + 1}"))
            .ToDictionary(t => t.Id, t => t.Label);

        List<Terminal> terminals = lattice.Cells
            .Where(c => isSource.Contains(c.Id) || isSink.Contains(c.Id))
            .Select(c => new Terminal
            {
                CellId = c.Id,
                IsSource = isSource.Contains(c.Id),
                IsSink = isSink.Contains(c.Id),
                SourceLabel = sourceLabels.GetValueOrDefault(c.Id),
                SinkLabel = sinkLabels.GetValueOrDefault(c.Id),
                SourceEnergy = isSource.Contains(c.Id) ? share : 0,
                SinkEnergy = sinkEnergy.GetValueOrDefault(c.Id),
            })
            .ToList();

        return new CompileResult
        {
            CoreEnergy = coreEnergy,
            UsedCost = used,
            Over = used > coreEnergy,
            WeaponEnergy = weaponEnergy,
            Sources = sourceCells.Select(c => c.Id).ToList(),
            Sinks = sinkCells.Select(c => c.Id).ToList(),
            Terminals = terminals,
            EdgeOps = edgeOps,
            Trace = order.Select(c => nodes[c.Id]).ToList(),
            Shot = shot,
        };
    }

    private static NodeTrace Trace(Cell cell, double cost, double inSum, double outE, double perOut,
        int outCount, bool isSource, bool isSink, bool fed, bool productive) => new()
    {
        CellId = cell.Id,
        Kind = cell.Kind,
        Orient = cell.Orient,
        Cost = cost,
        InSum = inSum,
        OutE = outE,
        PerOut = perOut,
        OutCount = outCount,
        IsSource = isSource,
        IsSink = isSink,
        Fed = fed,
        Productive = productive,
    };
}
