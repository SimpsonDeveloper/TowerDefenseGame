using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.towers.crystal.ui;

/// <summary>
/// Draws one crystal lattice and lets you edit it — the in-game builder and the template
/// editor's canvas are the same widget in two modes (`lattice-ui.md` §2–§4).
///
/// The preview is truthful by construction: it renders the very <see cref="CompileResult"/> the
/// runtime would fire, recompiled on every edit. Terminals are **displayed, never set** — the
/// green/orange badges are just <see cref="Lattice.IsSource"/> / <see cref="Lattice.IsSink"/>
/// asked again.
///
/// Deliberately thin: the framing, the y flip and the hit test all live in the engine-free
/// <see cref="LatticeCamera"/> where they are unit-tested. What is left here is Godot calls and
/// a <see cref="ViewPoint"/> ↔ <c>Vector2</c> conversion.
/// </summary>
public partial class LatticeView : Control
{
    /// <summary>Something was placed, removed, or painted — the host should refresh its readout.</summary>
    [Signal] public delegate void LatticeChangedEventHandler();

    [Export] public double CoreEnergy { get; set; } = 100;

    /// <summary>Blank border around the framed lattice, in pixels.</summary>
    [Export] public float Margin { get; set; } = 24;

    /// <summary>Draw each edge's combo op and multiplier.</summary>
    [Export] public bool ShowOps { get; set; } = true;

    /// <summary>Draw the energy leaving each crystal.</summary>
    [Export] public bool ShowEnergy { get; set; }

    /// <summary>
    /// Template-editor mode: clicks paint the MASK (which slots exist) instead of placing
    /// crystals. Off is the in-game builder.
    /// </summary>
    [Export] public bool PaintMask { get; set; }

    /// <summary>Rows of off-mask grid shown while painting, so the contour can be grown.</summary>
    [Export] public int PaintPadding { get; set; } = 2;

    public Lattice Lattice { get; private set; } = new Lattice();
    public LatticeMask Mask { get; private set; }
    public CompileResult Result { get; private set; }
    public CrystalKind SelectedKind { get; set; } = CrystalKind.Ruby;

    private readonly LatticeCamera _camera = new LatticeCamera(new LatticeGeometry(side: 1));
    private readonly List<CellCoord> _slots = new();

    public override void _Ready()
    {
        Resized += Rebuild;
        Rebuild();
    }

    /// <summary>Point the view at a lattice. A null mask means the whole grid is buildable.</summary>
    public void Setup(Lattice lattice, LatticeMask mask = null)
    {
        Lattice = lattice ?? new Lattice(mask);
        Mask = mask ?? Lattice.Mask;
        Rebuild();
    }

    /// <summary>Re-frame and recompile. Cheap — the lattice is a small finite DAG.</summary>
    public void Rebuild()
    {
        CollectSlots();
        _camera.Frame(_slots, Size.X, Size.Y, Margin);
        Result = Compiler.Compile(Lattice, CoreEnergy);
        QueueRedraw();
    }

    // ── layout ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which slots get drawn: the mask if there is one, else whatever has been built. While
    /// painting, pad outward so there is empty grid to grow the contour into.
    /// </summary>
    private void CollectSlots()
    {
        HashSet<CellCoord> slots = new();

        if (Mask != null)
        {
            foreach (CellCoord slot in Mask.Slots) slots.Add(slot);

            if (PaintMask)
            {
                CellBounds box = Mask.Bounds ?? new CellBounds(0, 3, 0, 6);
                // a column step is half a side, so pad twice as far horizontally to keep the
                // spare border looking even
                for (int row = box.MinRow - PaintPadding; row <= box.MaxRow + PaintPadding; row++)
                for (int col = box.MinCol - PaintPadding * 2; col <= box.MaxCol + PaintPadding * 2; col++)
                    slots.Add(new CellCoord(row, col));
            }
        }
        else
        {
            foreach (Cell cell in Lattice.Cells) slots.Add(cell.Coord);
        }

        if (slots.Count == 0)
            foreach (CellCoord slot in LatticeMask.Filled(rows: 4, cols: 7).Slots) slots.Add(slot);

        _slots.Clear();
        _slots.AddRange(slots.OrderBy(slot => slot.Height).ThenBy(slot => slot.Col));
    }

    private static Vector2 Px(ViewPoint point) => new((float)point.X, (float)point.Y);

    private Vector2 Centre(CellCoord coord) => Px(_camera.CenterOf(coord));

    private Vector2[] Triangle(CellCoord coord) => _camera.CornersOf(coord).Select(Px).ToArray();

    // ── drawing ──────────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        // Scale is pixels per triangle side. Text has to fit INSIDE a triangle, whose usable
        // width at the centroid is a fraction of that — and it must stop growing once the
        // lattice is small enough that the cells are already huge.
        int fontSize = Math.Clamp((int)(_camera.Scale * 0.13), 9, 17);

        DrawSlots(font, fontSize);
        if (Result == null) return;

        if (ShowOps) DrawOps(font, fontSize);
        DrawTerminals(font, fontSize);
    }

    private void DrawSlots(Font font, int fontSize)
    {
        foreach (CellCoord coord in _slots)
        {
            Vector2[] triangle = Triangle(coord);
            bool usable = Mask == null || Mask.IsUsable(coord);
            Cell cell = Lattice.At(coord.Row, coord.Col);

            Color fill = cell != null
                ? CrystalVisuals.Tint(cell.Kind)
                : usable ? CrystalVisuals.EmptySlot : CrystalVisuals.BlockedSlot;

            // a crystal left outside the mask is flagged here, not at save time — it compiles,
            // so nothing else would ever mention it
            bool orphan = cell != null && !usable;
            Color outline = orphan ? CrystalVisuals.Orphan
                : cell != null ? CrystalVisuals.CrystalOutline
                : CrystalVisuals.SlotOutline;

            DrawColoredPolygon(triangle, fill);
            DrawPolyline(
                triangle.Append(triangle[0]).ToArray(),
                outline,
                orphan ? 4f : usable ? 1.5f : 1f);

            if (cell == null) continue;

            // the crystal itself outranks every annotation drawn over it, so it is the one thing
            // drawn LARGER than the base size, in ink picked to survive its own fill
            Vector2 centre = Centre(coord);
            DrawLabel(font, CrystalVisuals.Glyph(cell.Kind), centre, fontSize + 5,
                CrystalVisuals.Ink(cell.Kind), plate: false);

            if (ShowEnergy && Result != null && Result.Energy.TryGetValue(cell.Id, out CellEnergy energy))
                DrawLabel(font, $"{energy.Out:0.#}",
                    centre + new Vector2(0, fontSize * 1.6f), fontSize - 2,
                    energy.InDebt ? CrystalVisuals.Debt : CrystalVisuals.OpText);
        }
    }

    /// <summary>
    /// One label per internal edge, drawn between the two crystals that name it. This is the
    /// per-edge trace the playground showed, on the real compile result.
    /// </summary>
    private void DrawOps(Font font, int fontSize)
    {
        foreach (EdgeOp op in Result.Ops)
        {
            Vector2 from = Centre(op.Upstream.Coord);
            Vector2 to = Centre(op.Downstream.Coord);
            Color color = op.Debt ? CrystalVisuals.Debt : CrystalVisuals.OpText;

            // the line is what ties a label to the pair that produced it — too faint and the
            // labels read as free-floating
            DrawLine(from, to, CrystalVisuals.Edge, 2f);
            DrawLabel(font, $"{Ops.Display(op.Op)} ×{op.Energy:0.#}",
                from.Lerp(to, 0.5f), fontSize - 2, color);
        }
    }

    /// <summary>
    /// Badges only. Terminals are geometry, not a setting — the builder cannot toggle one, so
    /// this method never writes anything (`lattice-ui.md` §3).
    /// </summary>
    private void DrawTerminals(Font font, int fontSize)
    {
        float radius = Math.Clamp((float)(_camera.Scale * 0.06), 4f, 11f);

        foreach (Terminal source in Result.Sources)
            Badge(font, source, CrystalVisuals.Source, -1, radius, fontSize);

        foreach (Terminal sink in Result.Sinks)
            Badge(font, sink, CrystalVisuals.Sink, 1, radius, fontSize);
    }

    private void Badge(Font font, Terminal terminal, Color color, int side, float radius, int fontSize)
    {
        // a lone crystal is BOTH source and sink, so the two badges sit on opposite sides of the
        // glyph and never land on top of each other
        Vector2 at = Centre(terminal.Cell.Coord) + new Vector2(side * radius * 2.4f, 0);
        DrawCircle(at, radius, color);
        DrawLabel(font, $"{terminal.Label} {terminal.Energy:0.#}",
            at + new Vector2(0, -radius * 2f), fontSize - 3, color);
    }

    /// <summary>
    /// Centred text on an opaque plate. The plate is not decoration: labels land on saturated
    /// crystal fills, where unbacked text of any colour is unreadable on some kind or other.
    /// </summary>
    private void DrawLabel(Font font, string text, Vector2 at, int fontSize, Color color, bool plate = true)
    {
        fontSize = Math.Max(8, fontSize);
        Vector2 extents = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        Vector2 topLeft = at - extents / 2;

        if (plate)
            DrawRect(new Rect2(topLeft - new Vector2(3, 1), extents + new Vector2(6, 2)),
                CrystalVisuals.Plate);

        // DrawString takes a BASELINE, which sits roughly 80% down the line box
        DrawString(font, new Vector2(topLeft.X, topLeft.Y + extents.Y * 0.8f),
            text, HorizontalAlignment.Left, -1, fontSize, color);
    }

    // ── interaction ──────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } click) return;

        CellCoord coord = _camera.Locate(new ViewPoint(click.Position.X, click.Position.Y));
        bool changed = click.ButtonIndex switch
        {
            MouseButton.Left => PaintMask ? Paint(coord, usable: true) : PlaceCrystal(coord),
            MouseButton.Right => PaintMask ? Paint(coord, usable: false) : Lattice.Remove(coord.Row, coord.Col),
            _ => false,
        };

        if (!changed) return;
        AcceptEvent();
        Rebuild();
        EmitSignal(SignalName.LatticeChanged);
    }

    /// <summary>Place the selected crystal, if the slot is on the mask and empty.</summary>
    private bool PlaceCrystal(CellCoord coord)
    {
        if (!Lattice.CanPlace(coord.Row, coord.Col)) return false;
        Lattice.Place(coord.Row, coord.Col, SelectedKind);
        return true;
    }

    /// <summary>Template editor: sculpt the contour itself.</summary>
    private bool Paint(CellCoord coord, bool usable)
    {
        if (Mask == null || Mask.IsUsable(coord) == usable) return false;

        if (usable) Mask.Allow(coord.Row, coord.Col);
        else Mask.Block(coord.Row, coord.Col);
        return true;
    }
}
