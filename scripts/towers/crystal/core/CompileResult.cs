using System.Collections.Generic;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>One combo fired on one active internal edge, scaled by the energy arriving downstream.</summary>
public sealed class EdgeOp
{
    public int UpCellId { get; init; }
    public int DownCellId { get; init; }
    public CrystalKind UpKind { get; init; }
    public CrystalKind DownKind { get; init; }
    public OpId Op { get; init; }

    /// <summary>Energy arriving at the downstream crystal, floored at 0.</summary>
    public double Energy { get; init; }

    /// <summary>The un-floored value was negative — the op is inert but the debt keeps flowing.</summary>
    public bool Debt { get; init; }

    // ordering keys for the shot (roadmap item 2), anchored to the DOWNSTREAM (producing) gem
    public int DownFlowDepth { get; init; }
    public int DownCol { get; init; }
    public int UpCol { get; init; }

    public override string ToString() =>
        $"{Ops.Display(Op)} ×{Energy:0.##}{(Debt ? " (debt)" : "")} @#{DownCellId}";
}

/// <summary>One entry of the compiled shot: an op and how much of it the hit carries.</summary>
public readonly record struct ShotOp(OpId Op, double Quantity)
{
    public override string ToString() => $"{Ops.Display(Op)} ×{Quantity:0.##}";
}

/// <summary>A crystal auto-classified as a terminal (compiler-core.md §2–§3).</summary>
public sealed class Terminal
{
    public int CellId { get; init; }
    public bool IsSource { get; init; }
    public bool IsSink { get; init; }

    /// <summary>S# for a source, T# for a sink (a lone crystal gets both).</summary>
    public string SourceLabel { get; init; }
    public string SinkLabel { get; init; }

    /// <summary>Seeded share for a source (E_core / nSources), delivered energy for a sink.</summary>
    public double SourceEnergy { get; init; }
    public double SinkEnergy { get; init; }
}

/// <summary>Per-crystal energy math — the trace the UI overlay renders.</summary>
public sealed class NodeTrace
{
    public int CellId { get; init; }
    public CrystalKind Kind { get; init; }
    public Orientation Orient { get; init; }
    public double Cost { get; init; }
    public double InSum { get; init; }
    public double OutE { get; init; }
    public double PerOut { get; init; }
    public int OutCount { get; init; }
    public bool IsSource { get; init; }
    public bool IsSink { get; init; }
    public bool Fed { get; init; }
    public bool Productive { get; init; }

    public bool Active => Fed && Productive;

    public override string ToString() =>
        $"#{CellId} {Kind} in={InSum:0.##} −{Cost:0.##} → {OutE:0.##}" +
        $"{(OutCount > 1 ? $" /{OutCount} = {PerOut:0.##}" : "")}";
}

/// <summary>What a lattice compiles to. Cached per tower; each shot applies it to the hit enemy.</summary>
public sealed class CompileResult
{
    public double CoreEnergy { get; init; }

    /// <summary>Sum of every placed crystal's cost.</summary>
    public double UsedCost { get; init; }

    /// <summary>Total cost exceeds the core energy.</summary>
    public bool Over { get; init; }

    /// <summary>Energy delivered to the weapon = Σ max(0, sink energy).</summary>
    public double WeaponEnergy { get; init; }

    /// <summary>Usable energy that dead-ended in a sinkless branch.</summary>
    public double LostEnergy { get; init; }

    /// <summary>Reserved for other legality concerns (e.g. the impact-count cap). Terminal rules
    /// hold by construction, so they never set this.</summary>
    public bool Legal { get; init; } = true;

    public IReadOnlyList<int> Sources { get; init; } = new List<int>();
    public IReadOnlyList<int> Sinks { get; init; } = new List<int>();
    public IReadOnlyList<Terminal> Terminals { get; init; } = new List<Terminal>();
    public IReadOnlyList<EdgeOp> EdgeOps { get; init; } = new List<EdgeOp>();
    public IReadOnlyList<NodeTrace> Trace { get; init; } = new List<NodeTrace>();

    /// <summary>
    /// The compiled shot — an ORDERED list of ops the enemy walks one at a time at hit time.
    /// STUBBED in roadmap item 1: the collect-and-order pass lands with op-flow (item 2).
    /// </summary>
    public IReadOnlyList<ShotOp> Shot { get; init; } = new List<ShotOp>();
}
