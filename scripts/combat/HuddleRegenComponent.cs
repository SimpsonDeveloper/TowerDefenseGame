using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// Enemies that crowd together heal. The first and so far only <b>enemy buff</b>
/// (<c>effect-vocab/vocab-overview/buffs.md</c>), and the thing Purify exists to answer.
///
/// <b>Natural, not applied.</b> Nothing grants this — it is a property of standing near your own
/// kind, so it has no source to kill and no duration to wait out. A pack is tougher than the sum
/// of its members, and the counterplay is to stop it being a pack: a longer maze, a split path, a
/// freeze on the leaders. That makes it the enemy's answer to AoE and the reason spacing is worth
/// anything (roadmap item 5, <c>impl-planning/combat/enemy-mind.md</c>).
///
/// <b>Derived, never stored.</b> The neighbour count is recomputed, never accumulated, so an enemy
/// that walks away from the pack stops healing by not being near it. The same reasoning as
/// <c>Shield-down</c> and movement scale: a buff written into a field is a buff something has to
/// remember to take away.
///
/// Scanned off the <c>enemies</c> group rather than through an <c>Area2D</c>, matching
/// <c>TowerPlacementManager</c>: it needs no shape wired into every enemy scene and it can honour
/// the viewport split, which matters because the pocket dimension is a second world and a pack
/// there must not heal a pack here. The cost is a full scan, so it runs on
/// <see cref="RecountInterval"/> rather than every frame — regen is slow enough that a count four
/// times a second is indistinguishable from a count sixty times a second.
/// </summary>
[GlobalClass]
public partial class HuddleRegenComponent : Node
{
    /// <summary>Where the healing lands. Without it the count still runs but nothing recovers.</summary>
    [Export] public HealthComponent Health;

    /// <summary>How close counts as crowded, in pixels.</summary>
    [Export] public float Radius { get; set; } = 48f;

    /// <summary>Fraction of max HP regained per second, <b>per neighbour</b>.</summary>
    [Export] public double FractionPerNeighbour { get; set; } = 0.01;

    /// <summary>
    /// Neighbours past which crowding stops helping. A cap, not a curve: without one a dense
    /// swarm out-heals anything, and the interesting case is a handful of enemies travelling
    /// together rather than a blob of thirty.
    /// </summary>
    [Export] public int MaxNeighbours { get; set; } = 3;

    /// <summary>Seconds between neighbour counts. Scales the whole system's cost directly.</summary>
    [Export] public double RecountInterval { get; set; } = 0.25;

    /// <summary>How many of its own kind are in range, as of the last count. Readouts and Purify.</summary>
    public int Neighbours { get; private set; }

    /// <summary>Is the buff actually doing anything right now?</summary>
    public bool IsRegenerating => Neighbours > 0 && Health != null && !Health.IsDead;

    /// <summary>Everything this enemy has healed back over its life. Readouts only.</summary>
    public double Regenerated { get; private set; }

    private Node2D _body;
    private double _untilRecount;

    public override void _Ready()
    {
        _body = GetParent() as Node2D;

        if (_body == null)
            GD.PushWarning($"[combat] {Name}: parent is not a Node2D — nothing to measure distance from");

        if (Health == null)
            GD.PushWarning($"[combat] {GetParent()?.Name} huddles but has no HealthComponent — regen will go nowhere");
    }

    public override void _Process(double delta)
    {
        _untilRecount -= delta;
        if (_untilRecount <= 0)
        {
            Neighbours = CountNeighbours();
            _untilRecount = RecountInterval;
        }

        if (!IsRegenerating) return;

        int counted = Neighbours < MaxNeighbours ? Neighbours : MaxNeighbours;
        double healed = Health.MaxHp * FractionPerNeighbour * counted * delta;

        Regenerated += healed;
        Health.Heal(healed);
    }

    /// <summary>
    /// Enemies of this enemy's own kind within <see cref="Radius"/>, excluding itself. Squared
    /// distances, so nothing takes a square root per pair.
    /// </summary>
    private int CountNeighbours()
    {
        if (_body == null) return 0;

        Viewport viewport = _body.GetViewport();
        Vector2 here = _body.GlobalPosition;
        float radiusSquared = Radius * Radius;
        int found = 0;

        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node == _body || node is not Node2D other) continue;

            // Two worlds run at once; a pack in the pocket dimension is not standing next to this
            // one however close their coordinates look.
            if (other.GetViewport() != viewport) continue;

            if (here.DistanceSquaredTo(other.GlobalPosition) <= radiusSquared) found++;
        }

        return found;
    }
}
