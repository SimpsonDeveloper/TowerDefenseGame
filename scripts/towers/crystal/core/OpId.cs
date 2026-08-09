using System.Collections.Generic;

namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// The 21 op names a crystal pair can produce, plus <see cref="None"/> for "no op".
/// Naming only — an op's behavior is authored per-file under
/// <c>docs/tower-design/effect-vocab/ops/</c> and implemented in the combat layer
/// (roadmap item 4), never here.
/// </summary>
public enum OpId
{
    None = 0,

    // primitives (7) — apply a state / base effect and stand alone
    Burn,
    ChillFreeze,
    Corrode,
    Mark,
    Scramble,
    MindDamage,
    Purify,

    // interactives (14) — reactive; may consume a state at HIT TIME (not in the compiler)
    Frostburn,
    Shatter,
    Flareup,
    Dissolve,
    Detonate,
    Focus,
    Hex,
    FireArc,
    FrostArc,
    AcidArc,
    Numb,
    Accelerant,
    Weather,
    ShortCircuit,
}

/// <summary>Op classification + display names (the strings used in the design docs).</summary>
public static class Ops
{
    private static readonly HashSet<OpId> Primitives = new()
    {
        OpId.Burn, OpId.ChillFreeze, OpId.Corrode, OpId.Mark,
        OpId.Scramble, OpId.MindDamage, OpId.Purify,
    };

    public static bool IsPrimitive(OpId op) => Primitives.Contains(op);

    public static bool IsInteractive(OpId op) => op != OpId.None && !Primitives.Contains(op);

    /// <summary>Doc-facing name, matching combo-matrix.md exactly (for traces and diffs).</summary>
    public static string Display(OpId op) => op switch
    {
        OpId.None => "—",
        OpId.ChillFreeze => "Chill → Freeze",
        OpId.MindDamage => "Mind-damage",
        OpId.FireArc => "Fire Arc",
        OpId.FrostArc => "Frost Arc",
        OpId.AcidArc => "Acid Arc",
        OpId.ShortCircuit => "Short-circuit",
        _ => op.ToString(),
    };
}
