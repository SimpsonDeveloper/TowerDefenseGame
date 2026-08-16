using System.Collections.Generic;
using System.Linq;
using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.towers.crystal;

/// <summary>
/// A saved lattice **shape plus the crystals it starts with** — the design-time asset a tower
/// type will point at for its default configuration (`lattice-ui.md` §4).
///
/// This is authored, ships in <c>res://</c>, and is read-only at runtime. It is deliberately
/// **not** a save-game: a player's evolving lattice is separate state, because overwriting the
/// template with it would destroy the very thing "reset to default" resets to. The two share
/// this serialization, nothing else.
///
/// All the logic lives in <see cref="LatticeSnapshot"/>, which is engine-free and tested. This
/// class is the <c>.tres</c> shell around it and holds no rules.
/// </summary>
[GlobalClass]
public partial class CrystalTemplate : Resource
{
    [Export] public string DisplayName { get; set; } = "Untitled";

    /// <summary>Usable slots as <c>(row, col)</c> — the buildable contour.</summary>
    [Export] public Godot.Collections.Array<Vector2I> Mask { get; set; } = new();

    /// <summary>
    /// Starting crystals as <c>(row, col, kind)</c>, where <c>z</c> indexes
    /// <see cref="CrystalKind"/>. Packed rather than one sub-resource each so a template stays a
    /// couple of readable lines in a diff. The roster is effectively frozen — reordering it would
    /// already invalidate <see cref="ComboMatrix"/>'s table — so the index is safe to store.
    /// </summary>
    [Export] public Godot.Collections.Array<Vector3I> Crystals { get; set; } = new();

    public static CrystalTemplate From(Lattice lattice, string displayName = "Untitled")
    {
        LatticeSnapshot snapshot = LatticeSnapshot.Of(lattice);

        return new CrystalTemplate
        {
            DisplayName = displayName,
            Mask = new Godot.Collections.Array<Vector2I>(
                snapshot.Mask.Select(slot => new Vector2I(slot.Row, slot.Col))),
            Crystals = new Godot.Collections.Array<Vector3I>(
                snapshot.Crystals.Select(c => new Vector3I(c.Row, c.Col, (int)c.Kind))),
        };
    }

    public LatticeSnapshot ToSnapshot()
    {
        int kinds = System.Enum.GetValues<CrystalKind>().Length;

        List<PlacedCrystal> crystals = Crystals
            .Where(packed => packed.Z >= 0 && packed.Z < kinds)   // a hand-edited file can lie
            .Select(packed => new PlacedCrystal(packed.X, packed.Y, (CrystalKind)packed.Z))
            .ToList();

        if (crystals.Count != Crystals.Count)
            GD.PushWarning($"{ResourcePath}: dropped {Crystals.Count - crystals.Count} crystal(s) with an unknown kind.");

        return new LatticeSnapshot(
            Mask.Select(slot => new CellCoord(slot.X, slot.Y)),
            crystals);
    }

    /// <summary>Rebuild the lattice. Throws if the file describes something unbuildable.</summary>
    public Lattice ToLattice() => ToSnapshot().Restore();
}
