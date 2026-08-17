using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.towers.crystal.ui;

/// <summary>
/// How the lattice looks. The one place a crystal's colour is decided, so the builder, the
/// template editor and any future tooltip agree. Colours mirror <c>CRYSTALS</c> in
/// <c>docs/tower-design/playground/archive/crystal-core.js</c>.
///
/// Presentation only — no rule lives here. The engine-free core has no idea these exist.
/// </summary>
public static class CrystalVisuals
{
    public static Color Tint(CrystalKind kind) => kind switch
    {
        CrystalKind.Ruby => new Color("e6394a"),
        CrystalKind.Sapphire => new Color("3aa0ff"),
        CrystalKind.Emerald => new Color("2ecc71"),
        CrystalKind.Citrine => new Color("f1c40f"),
        CrystalKind.Amethyst => new Color("a974ff"),
        _ => new Color("d7e1f4"),
    };

    /// <summary>Single letter drawn on the triangle — the roster has no two kinds sharing one.</summary>
    public static string Glyph(CrystalKind kind) => kind.ToString()[..1];

    /// <summary>
    /// Ink that reads on top of <see cref="Tint"/>. Six saturated fills spanning near-white
    /// Quartz to deep Ruby have no single legible ink, so pick by perceived brightness.
    /// </summary>
    public static Color Ink(CrystalKind kind)
    {
        Color tint = Tint(kind);
        float luminance = 0.299f * tint.R + 0.587f * tint.G + 0.114f * tint.B;
        return luminance > 0.6f ? new Color("11141c") : new Color("ffffff");
    }

    // ── lattice chrome ───────────────────────────────────────────────────────────

    /// <summary>A slot the mask allows but nothing occupies.</summary>
    public static readonly Color EmptySlot = new Color("1c2333");

    /// <summary>A slot outside the mask — permanently unbuildable (`lattice-ui.md` §1).</summary>
    public static readonly Color BlockedSlot = new Color("0d1018");

    public static readonly Color SlotOutline = new Color("38455f");

    public static readonly Color CrystalOutline = new Color("0b0e14");

    /// <summary>Source badge: a leaf-input crystal the core seeds. Never user-set.</summary>
    public static readonly Color Source = new Color("58d68d");

    /// <summary>Sink badge: a leaf-output crystal that drains to the weapon.</summary>
    public static readonly Color Sink = new Color("f0932b");

    public static readonly Color Edge = new Color("7f8fa6");

    public static readonly Color OpText = new Color("dfe6e9");

    /// <summary>An edge whose upstream crystal ran into debt — it fires nothing.</summary>
    public static readonly Color Debt = new Color("ff7675");

    /// <summary>
    /// Backing behind every label. Text sits on top of saturated crystal fills, so without an
    /// opaque plate a red op label on a Ruby is invisible.
    /// </summary>
    public static readonly Color Plate = new Color(0.04f, 0.05f, 0.08f, 0.85f);
}
