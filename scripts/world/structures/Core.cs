using Godot;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.towers;

namespace towerdefensegame.scripts.world.structures;

/// <summary>
/// What the enemies are coming for, and what a leak costs. One per map.
///
/// It is the destination of last resort: an enemy with no route laid for it, and an enemy that
/// has broken the illusion and abandoned the route, both head here
/// (<c>impl-planning/combat/paths.md</c>). Until there is a core there is no "toward" for an
/// enemy to walk in, which is why this exists before deviation does.
///
/// <b>A structure, not a tower.</b> It carries a <see cref="HealthComponent"/> and registers a
/// footprint like a tower does — so enemies path to it and the placement system will not drop a
/// turret on top of it — but it is placed by the map rather than the player, cannot be destroyed
/// by <c>Destroy Mode</c>, and has no crystal lattice. It joins the <c>Structures</c> group, which
/// is what an enemy's <c>AttackerComponent</c> hits, and the <c>Core</c> group, which is how
/// anything finds it. It deliberately does <b>not</b> join <c>Towers</c>: that group means "a
/// thing the player built and can edit", and half the tower systems assume it.
///
/// A <c>StaticBody2D</c> on the same layer as a tower (16), because an enemy's attacker finds its
/// victims through <c>GetOverlappingBodies</c> and the nav bake carves around collision. A plain
/// <c>Node2D</c> here would be unhittable and enemies would walk through it.
/// </summary>
[GlobalClass]
public partial class Core : StaticBody2D
{
    /// <summary>The group an enemy's attacker looks for. Towers are in it too.</summary>
    public const string StructuresGroup = "Structures";

    /// <summary>How anything finds the core. Exactly one node should be in it per viewport.</summary>
    public const string CoreGroup = "Core";

    [Export] public HealthComponent Health { get; set; }

    /// <summary>
    /// The core's square in world space. Anything joining onto the core wants a point on this
    /// edge rather than the centre, which is inside the hole the nav bake carves.
    /// </summary>
    public Rect2 FootprintRect =>
        new(GlobalPosition - (Vector2)SizePixels * 0.5f, SizePixels);

    [Export] public CoordConfig Coords { get; set; }

    /// <summary>Registers the footprint so enemies can path to it and towers cannot overlap it.</summary>
    [Export] public TowerFootprintTracker FootprintTracker { get; set; }

    /// <summary>Footprint side, in pixels. Square, snapped like a tower's.</summary>
    [Export] public Vector2I SizePixels { get; set; } = new(32, 32);

    /// <summary>The core fell. Nothing listens yet — there is no lose condition to raise.</summary>
    [Signal] public delegate void FallenEventHandler();

    public override void _Ready()
    {
        AddToGroup(StructuresGroup);
        AddToGroup(CoreGroup);

        if (Health == null)
        {
            GD.PushWarning($"{Name}: no HealthComponent — the core cannot be damaged.");
        }
        else
        {
            Health.Destroyed += OnDestroyed;
        }

        RegisterFootprint();
    }

    public override void _ExitTree()
    {
        if (Health != null) Health.Destroyed -= OnDestroyed;
        FootprintTracker?.Unregister(this);
    }

    /// <summary>
    /// Claim the tiles under the core. Shares <see cref="TowerSnapHelper"/> with towers rather
    /// than inventing a second footprint convention, so "cannot build here" means one thing.
    /// </summary>
    private void RegisterFootprint()
    {
        if (FootprintTracker == null || Coords == null) return;

        var tiles = new System.Collections.Generic.List<Vector2I>(
            TowerSnapHelper.FootprintTiles(GlobalPosition, SizePixels, Coords));

        FootprintTracker.Register(this, tiles);
    }

    private void OnDestroyed()
    {
        GD.Print("[core] the core has fallen");
        EmitSignal(SignalName.Fallen);
    }

    /// <summary>The core in this viewport, or null. Walked fresh — there is one node and it never moves.</summary>
    public static Core ForViewport(Viewport viewport)
    {
        if (viewport == null) return null;

        foreach (Node node in viewport.GetTree().GetNodesInGroup(CoreGroup))
            if (node is Core core && core.GetViewport() == viewport) return core;

        return null;
    }
}
