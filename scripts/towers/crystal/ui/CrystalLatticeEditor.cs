using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using towerdefensegame.scripts.towers.crystal.core;
using towerdefensegame.scripts.towers.crystal;

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

    /// <summary>Where authored templates live. Design-time assets, so <c>res://</c>.</summary>
    private const string TemplateDir = "res://resources/crystal_templates";

    private LatticeView _view;
    private Label _readout;
    private Button _selected;
    private LineEdit _templateName;
    private Label _status;
    private Label _hint;
    private FileDialog _dialog;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildUI();
        ShowHint();
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

        _dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Resources,   // templates ship with the build
            Filters = new[] { "*.tres ; Crystal template" },
            UseNativeDialog = false,
        };
        _dialog.FileSelected += OnFileChosen;
        AddChild(_dialog);
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

        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_hint);
        panel.AddChild(new HSeparator());

        // the two modes bind the SAME two buttons to different verbs, so the hint has to follow
        CheckBox paint = new CheckBox { Text = "Paint mask" };
        paint.Toggled += on =>
        {
            _view.PaintMask = on;
            _view.Rebuild();
            ShowHint();
            Refresh();
        };
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

        panel.AddChild(new HSeparator());
        panel.AddChild(new Label { Text = "Template" });

        _templateName = new LineEdit { Text = "Untitled", PlaceholderText = "name" };
        panel.AddChild(_templateName);

        Button save = new Button { Text = "Save…" };
        save.Pressed += () => ShowFileDialog(FileDialog.FileModeEnum.SaveFile);
        panel.AddChild(save);

        Button load = new Button { Text = "Load…" };
        load.Pressed += () => ShowFileDialog(FileDialog.FileModeEnum.OpenFile);
        panel.AddChild(load);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_status);

        return outer;
    }

    // ── templates ────────────────────────────────────────────────────────────────

    private void ShowFileDialog(FileDialog.FileModeEnum mode)
    {
        DirAccess.MakeDirRecursiveAbsolute(TemplateDir);

        _dialog.FileMode = mode;
        _dialog.Title = mode == FileDialog.FileModeEnum.SaveFile ? "Save template" : "Load template";
        _dialog.CurrentDir = TemplateDir;
        _dialog.CurrentFile = $"{_templateName.Text.ToLowerInvariant().Replace(' ', '_')}.tres";
        _dialog.PopupCentered(new Vector2I(760, 520));
    }

    private void OnFileChosen(string path)
    {
        if (_dialog.FileMode == FileDialog.FileModeEnum.SaveFile) Save(path);
        else Load(path);
    }

    private void Save(string path)
    {
        CrystalTemplate template = CrystalTemplate.From(_view.Lattice, _templateName.Text);
        Error error = ResourceSaver.Save(template, path);

        Report(error == Error.Ok
            ? $"saved {template.Mask.Count} slots / {template.Crystals.Count} crystals"
            : $"save failed: {error}");
    }

    private void Load(string path)
    {
        CrystalTemplate template = ResourceLoader.Load<CrystalTemplate>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (template == null) { Report("not a crystal template"); return; }

        // a hand-edited file can describe something unbuildable — say so instead of half-loading
        LatticeSnapshot snapshot = template.ToSnapshot();
        IReadOnlyList<string> problems = snapshot.Problems();
        if (problems.Count > 0) { Report(string.Join("\n", problems)); return; }

        Lattice lattice = snapshot.Restore();
        _templateName.Text = template.DisplayName;
        _view.Setup(lattice, lattice.Mask);
        Refresh();
        Report($"loaded \"{template.DisplayName}\"");
    }

    private void Report(string message)
    {
        _status.Text = message;
        GD.Print($"[lattice] {message}");
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

    private void ShowHint() => _hint.Text = _view.PaintMask
        ? "left click: add cell\nright click: remove cell"
        : "left click: place\nright click: remove";

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

        // surfaced here as well as on the lattice, because it is the reason a Save will refuse
        int orphans = _view.Lattice.OffMask().Count();
        string warning = orphans == 0 ? "" : $"\n{orphans} crystal(s) outside the mask — cannot save";

        string shot = result.Shot.Count == 0
            ? "  (no combos yet)"
            : string.Join("\n", result.Shot.Select((ShotOp op, int i) => $"  {i + 1}. {op}"));

        _readout.Text = string.Join("\n", new[]
        {
            $"core       {result.CoreEnergy:0.#}",
            $"crystals   {_view.Lattice.Cells.Count}",
            $"cost       {result.UsedCost:0.#}" + (result.Over ? "   OVER BUDGET" : ""),
            $"weapon     {result.WeaponEnergy:0.#}" + warning,
            "",
            $"sources  {string.Join(", ", result.Sources.Select(t => t.Label))}",
            $"sinks    {string.Join(", ", result.Sinks.Select(t => t.Label))}",
            "",
            "shot (fires in order):",
            shot,
        });
    }
}
