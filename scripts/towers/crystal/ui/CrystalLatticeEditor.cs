using System;
using System.Linq;
using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.towers.crystal.ui;

/// <summary>
/// The dev-facing shell around <see cref="LatticeView"/> — the in-engine successor to the
/// archived browser playground (`lattice-ui.md`). A crystal palette on the left, the lattice in
/// the middle, and the live compile readout on the right.
///
/// Both surfaces the doc describes live here behind one toggle: **build** places crystals on a
/// fixed mask, **paint mask** sculpts the contour itself (§4). The in-game builder is the same
/// <see cref="LatticeView"/> with painting off.
/// </summary>
public partial class CrystalLatticeEditor : Control
{
    [Export] public int MaskRows { get; set; } = 4;
    [Export] public int MaskCols { get; set; } = 7;
    /// <summary>
    /// Enough to actually light a full default mask. Shipping costs are steep (a Ruby is 28), so
    /// a 4×7 board runs ~300 — at 100 the whole lattice is in debt and every op reads ×0, which
    /// is correct but useless to look at.
    /// </summary>
    [Export] public double CoreEnergy { get; set; } = 600;

    private LatticeView _view;
    private Label _readout;
    private Button _selected;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildUI();
        NewLattice();
    }

    // ── UI construction ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        ColorRect background = new ColorRect { Color = new Color("11141c") };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        HBoxContainer columns = new HBoxContainer();
        columns.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(columns);

        columns.AddChild(BuildPalette());

        _view = new LatticeView
        {
            CoreEnergy = CoreEnergy,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _view.LatticeChanged += Refresh;
        columns.AddChild(_view);

        columns.AddChild(BuildReadout());
    }

    private Control BuildPalette()
    {
        MarginContainer outer = new MarginContainer { CustomMinimumSize = new Vector2(172, 0) };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            outer.AddThemeConstantOverride($"margin_{side}", 10);

        VBoxContainer panel = new VBoxContainer();
        outer.AddChild(panel);

        panel.AddChild(new Label { Text = "Crystal" });
        foreach (CrystalKind kind in Enum.GetValues<CrystalKind>())
        {
            CrystalKind captured = kind;
            Button button = new Button
            {
                Text = $"{kind}  ({CrystalStats.Default.Cost(kind):0})",
                Modulate = CrystalVisuals.Tint(kind),
            };
            button.Pressed += () => SelectKind(captured, button);
            panel.AddChild(button);
            if (kind == CrystalKind.Ruby) _selected = button;
        }

        panel.AddChild(new HSeparator());
        panel.AddChild(new Label { Text = "left click: place\nright click: remove" });
        panel.AddChild(new HSeparator());

        CheckBox paint = new CheckBox { Text = "Paint mask" };
        paint.Toggled += on => { _view.PaintMask = on; _view.Rebuild(); Refresh(); };
        panel.AddChild(paint);

        CheckBox ops = new CheckBox { Text = "Show ops", ButtonPressed = true };
        ops.Toggled += on => { _view.ShowOps = on; _view.QueueRedraw(); };
        panel.AddChild(ops);

        CheckBox energy = new CheckBox { Text = "Show energy" };
        energy.Toggled += on => { _view.ShowEnergy = on; _view.QueueRedraw(); };
        panel.AddChild(energy);

        panel.AddChild(new HSeparator());
        panel.AddChild(new Label { Text = "Core energy" });

        SpinBox core = new SpinBox { MinValue = 0, MaxValue = 5000, Step = 25, Value = CoreEnergy };
        core.ValueChanged += value => { _view.CoreEnergy = value; _view.Rebuild(); Refresh(); };
        panel.AddChild(core);

        Button reset = new Button { Text = "Clear" };
        reset.Pressed += NewLattice;
        panel.AddChild(reset);

        return outer;
    }

    private Control BuildReadout()
    {
        PanelContainer panel = new PanelContainer { CustomMinimumSize = new Vector2(230, 0) };
        MarginContainer margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 10);
        panel.AddChild(margin);

        _readout = new Label { VerticalAlignment = VerticalAlignment.Top };
        margin.AddChild(_readout);
        return panel;
    }

    private void SelectKind(CrystalKind kind, Button button)
    {
        _view.SelectedKind = kind;
        if (_selected != null) _selected.Flat = false;
        _selected = button;
        button.Flat = true;
    }

    // ── state ────────────────────────────────────────────────────────────────────

    private void NewLattice()
    {
        LatticeMask mask = LatticeMask.Filled(MaskRows, MaskCols);
        _view.Setup(new Lattice(mask), mask);
        Refresh();
    }

    /// <summary>
    /// Mirrors the playground's trace panel, on the result the runtime would actually fire —
    /// the preview is the compiler, not a model of it.
    /// </summary>
    private void Refresh()
    {
        CompileResult result = _view.Result;
        if (result == null) return;

        string shot = result.Shot.Count == 0
            ? "  (no combos yet)"
            : string.Join("\n", result.Shot.Select((ShotOp op, int i) => $"  {i + 1}. {op}"));

        _readout.Text = string.Join("\n", new[]
        {
            $"core       {result.CoreEnergy:0.#}",
            $"crystals   {_view.Lattice.Cells.Count}",
            $"cost       {result.UsedCost:0.#}" + (result.Over ? "   OVER BUDGET" : ""),
            $"weapon     {result.WeaponEnergy:0.#}",
            "",
            $"sources  {string.Join(", ", result.Sources.Select(t => t.Label))}",
            $"sinks    {string.Join(", ", result.Sinks.Select(t => t.Label))}",
            "",
            "shot (fires in order):",
            shot,
        });
    }
}
