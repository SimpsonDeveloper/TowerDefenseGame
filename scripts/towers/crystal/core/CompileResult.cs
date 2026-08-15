using System.Collections.Generic;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// What the routing sweep worked out for one crystal. <c>In</c> is what its in-neighbours
/// handed up (or the seeded share, if it is a source), <c>Out</c> is what survives its own toll.
/// </summary>
public readonly record struct CellEnergy(double In, double Cost, double Out, int OutCount)
{
    /// <summary>
    /// What each out-neighbour receives: a ▲ divides its post-toll energy between its two
    /// outputs, a ▽ hands its single output the lot. Sinks have no outputs and nothing reads
    /// this. Debt passes through **unclamped** so a ▽ downstream can sum it back above 0.
    /// </summary>
    public double PerOut => OutCount > 0 ? Out / OutCount : 0;

    /// <summary>Post-toll energy went negative — ops here are inert, but the debt still flows.</summary>
    public bool InDebt => Out < 0;

    public override string ToString() =>
        $"in {In:0.##} − {Cost:0.##} = {Out:0.##}" + (OutCount > 1 ? $" ÷{OutCount} = {PerOut:0.##}" : "");
}

/// <summary>
/// A crystal at the lattice boundary. Derived from topology every compile
/// (<see cref="Lattice.IsSource"/> / <see cref="Lattice.IsSink"/>), never set by the player.
/// </summary>
/// <param name="Cell">The crystal.</param>
/// <param name="Label">`S1`, `S2`, … for sources; `T1`, `T2`, … for sinks.</param>
/// <param name="Energy">A source's seeded share, or a sink's delivered energy (floored at 0).</param>
public sealed record Terminal(Cell Cell, string Label, double Energy);

/// <summary>
/// One combo, fired by the crystal pair on one internal edge. <see cref="Energy"/> is what
/// crossed that edge — the upstream crystal's per-output share — floored at 0.
/// </summary>
public sealed record EdgeOp(Cell Upstream, Cell Downstream, OpId Op, double Energy, bool Debt)
{
    public override string ToString() =>
        $"{Ops.Display(Op)} ×{Energy:0.##}{(Debt ? " (debt)" : "")} @{Downstream}";
}

/// <summary>One entry of the compiled shot: an op and how much of it the hit carries.</summary>
public readonly record struct ShotOp(OpId Op, double Quantity)
{
    public override string ToString() => $"{Ops.Display(Op)} ×{Quantity:0.##}";
}

/// <summary>
/// What a lattice compiles to. Cached per tower; each shot applies it to the enemy it hits.
/// </summary>
public sealed class CompileResult
{
    public double CoreEnergy { get; init; }

    /// <summary>Sum of every placed crystal's cost.</summary>
    public double UsedCost { get; init; }

    /// <summary>Energy delivered to the weapon = Σ sink energy = <c>CoreEnergy − UsedCost</c>.</summary>
    public double WeaponEnergy { get; init; }

    /// <summary>Total draw exceeds the core, so the stream falls into debt somewhere.</summary>
    public bool Over => UsedCost > CoreEnergy;

    public IReadOnlyList<Terminal> Sources { get; init; } = new List<Terminal>();
    public IReadOnlyList<Terminal> Sinks { get; init; } = new List<Terminal>();

    /// <summary>One entry per internal edge, in bottom→top order.</summary>
    public IReadOnlyList<EdgeOp> Ops { get; init; } = new List<EdgeOp>();

    /// <summary>Per-crystal routing math, keyed by <see cref="Cell.Id"/> — the UI's trace.</summary>
    public IReadOnlyDictionary<int, CellEnergy> Energy { get; init; } = new Dictionary<int, CellEnergy>();

    /// <summary>
    /// The compiled shot — an ORDERED list of ops the enemy walks one at a time at hit time,
    /// lowest-producing gem first. Nothing here is consumed: a primitive and the interactive
    /// that will eat it both appear, and the enemy resolves that when the shot lands.
    /// </summary>
    public IReadOnlyList<ShotOp> Shot { get; init; } = new List<ShotOp>();
}
