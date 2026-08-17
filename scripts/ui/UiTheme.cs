using Godot;

namespace towerdefensegame.scripts.ui;

/// <summary>
/// The game's <c>Theme</c> resources and the theme type variations inside them.
///
/// A variation is a custom theme type whose <c>base_type</c> is a built-in one — set
/// <c>Control.ThemeTypeVariation</c> to its name and that control styles independently of every
/// other control of its class, with no subclass involved. The names are string keys into a
/// <c>.tres</c>, so they are consts here rather than literals at the call site: a typo silently
/// falls back to the base type and looks merely wrong.
/// </summary>
public static class UiTheme
{
    /// <summary>Chrome for the crystal lattice editor. Assign on the editor's root Control;
    /// a theme cascades to every descendant.</summary>
    public const string CrystalEditor = "res://resources/ui/crystal_editor_theme.tres";

    /// <summary>A crystal button in the palette. Per-kind colour is painted on top in code.</summary>
    public const string Swatch = "CrystalSwatch";

    /// <summary>The armed crystal — the kind a click would place.</summary>
    public const string SwatchSelected = "CrystalSwatchSelected";

    /// <summary>
    /// Load a theme, or <c>null</c> if it is missing. Missing is survivable: Godot falls back to
    /// the default theme, so the editor is ugly rather than broken, which is the right trade for
    /// a tool.
    /// </summary>
    public static Theme Load(string path)
    {
        Theme theme = ResourceLoader.Load<Theme>(path);
        if (theme == null) GD.PushWarning($"[ui] no theme at {path} — falling back to Godot's default");
        return theme;
    }
}
