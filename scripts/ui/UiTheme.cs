using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.ui;

/// <summary>
/// The game's <c>Theme</c> resources and the theme type variations inside them.
///
/// A variation is a custom theme type whose <c>base_type</c> is a built-in one — set
/// <c>Control.ThemeTypeVariation</c> to its name and that control styles independently of every
/// other control of its class, with no subclass involved. It inherits every item the base type
/// defines, so a variation only has to name what it changes.
///
/// The names below are string keys into a <c>.tres</c>, which is why they are consts rather than
/// literals at the call site: a typo does not fail, it silently falls back to the base type and
/// merely looks plain.
/// </summary>
public static class UiTheme
{
    /// <summary>
    /// Chrome for the crystal lattice editor. Assign on the editor's root Control — a theme
    /// cascades to every descendant, so controls built in code ask for nothing and inherit
    /// everything.
    ///
    /// Authored in Godot's Theme editor (open the <c>.tres</c>; the panel appears at the bottom,
    /// beside Output and Debugger). Two notes for editing it there:
    ///
    /// <list type="bullet">
    ///   <item><b>Base type is not editable in that panel.</b> <c>CrystalSwatch/base_type</c> lives
    ///     in the file's text only. If a save ever drops it, the swatches lose everything the
    ///     variations do not define outright.</item>
    ///   <item><b>Add Preview</b> takes <c>scenes/crystal_lattice_editor.tscn</c>, so the real
    ///     editor updates live instead of the generic control gallery.</item>
    /// </list>
    /// </summary>
    public const string CrystalEditor = "res://resources/ui/crystal_editor_theme.tres";

    /// <summary>
    /// The palette button for one crystal kind — <c>CrystalSwatchRuby</c> and friends. One
    /// variation per kind, because a <c>StyleBoxFlat</c>'s colour is a plain value and cannot
    /// point at anything shared, so six colours need six types.
    ///
    /// <b>Nothing about these is set from code.</b> The swatches are toggle buttons in a
    /// <c>ButtonGroup</c>, so Godot keeps exactly one pressed and <c>pressed</c> IS the selected
    /// look — a state the theme already styles. All the editor does is name the variation.
    ///
    /// The cost of authoring in the theme rather than deriving from <c>CrystalVisuals</c>: these
    /// colours and the ones the lattice draws with are kept matching <b>by hand</b>. A new
    /// <see cref="CrystalKind"/> without a matching type here falls back to a plain
    /// <c>Button</c> — <see cref="MissingSwatches"/> exists to make that loud rather than subtle.
    /// </summary>
    public static string Swatch(CrystalKind kind) => $"CrystalSwatch{kind}";

    /// <summary>
    /// Kinds the theme has no variation for. A missing type is not an error to Godot — the
    /// control silently falls back to its base type and merely looks plain — so it has to be
    /// looked for on purpose.
    /// </summary>
    public static IEnumerable<CrystalKind> MissingSwatches(Theme theme)
    {
        if (theme == null) yield break;

        foreach (CrystalKind kind in Enum.GetValues<CrystalKind>())
            if (!theme.GetTypeList().Contains(Swatch(kind)))
                yield return kind;
    }

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
