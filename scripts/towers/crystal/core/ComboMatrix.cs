namespace towerdefensegame.scripts.towers.crystal.core;

/// <summary>
/// Pair of crystals → the op they produce. Symmetric: <c>ComboOp(a,b) == ComboOp(b,a)</c>.
/// The diagonal is a crystal's single-crystal native op (two of that crystal adjacent).
/// Source of truth: <c>docs/tower-design/effect-vocab/vocab-overview/combo-matrix.md</c>
/// (mirrored in <c>playground/crystal-core.js</c>).
/// </summary>
public static class ComboMatrix
{
    private const int N = 6;

    // indexed [a, b] in CrystalKind order: Ruby, Sapphire, Emerald, Citrine, Amethyst, Quartz
    private static readonly OpId[,] Table = new OpId[N, N]
    {
        // Ruby
        { OpId.Burn,       OpId.Frostburn,   OpId.Accelerant, OpId.FireArc,  OpId.Mark,       OpId.Flareup },
        // Sapphire
        { OpId.Frostburn,  OpId.ChillFreeze, OpId.Weather,    OpId.FrostArc, OpId.Numb,       OpId.Shatter },
        // Emerald
        { OpId.Accelerant, OpId.Weather,     OpId.Corrode,    OpId.AcidArc,  OpId.Hex,        OpId.Dissolve },
        // Citrine
        { OpId.FireArc,    OpId.FrostArc,    OpId.AcidArc,    OpId.Scramble, OpId.Detonate,   OpId.ShortCircuit },
        // Amethyst
        { OpId.Mark,       OpId.Numb,        OpId.Hex,        OpId.Detonate, OpId.MindDamage, OpId.Focus },
        // Quartz
        { OpId.Flareup,    OpId.Shatter,     OpId.Dissolve,   OpId.ShortCircuit, OpId.Focus,  OpId.Purify },
    };

    /// <summary>The op a pair of crystals adjacent in the flow produces (order-independent).</summary>
    public static OpId ComboOp(CrystalKind a, CrystalKind b) => Table[(int)a, (int)b];

    /// <summary>A crystal's native op — its diagonal cell.</summary>
    public static OpId NativeOp(CrystalKind kind) => ComboOp(kind, kind);
}
