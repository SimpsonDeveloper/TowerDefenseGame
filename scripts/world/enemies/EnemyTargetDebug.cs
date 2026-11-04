using Godot;
using towerdefensegame.scripts.world.enemies.targeting;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Draws what an enemy is thinking about: the ring it will break off inside, and a line to
/// whatever it is currently walking toward.
///
/// A development readout, not a game visual — turn it off with <see cref="Enabled"/> and nothing
/// about behaviour changes. It exists because aggro radius is a <c>float</c> compared against
/// <c>DistanceSquaredTo</c> inside the targeter, not a collision shape, so Godot's own "Visible
/// Collision Shapes" cannot show it and there is otherwise no way to see why an enemy turned.
///
/// The colour is the state, so one glance answers the only question worth asking:
/// <b>aqua</b> heading for the core, <b>orange</b> broken off onto a turret, <b>grey</b> nothing
/// reachable. A railroaded enemy draws no ring at all, because it cannot break off whatever it
/// passes.
///
/// Immediate-mode <c>Node2D</c>, like <c>EnemyStateDebug</c> and the HP bar, so nothing here
/// touches the UI theme or has any opinion about it.
/// </summary>
[GlobalClass]
public partial class EnemyTargetDebug : Node2D
{
    /// <summary>The controller that owns the targeter. Read-only — this node never steers.</summary>
    [Export] public EnemyNavController Controller;

    /// <summary>Off means not drawn at all. No cost.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>Draw the line to the current target as well as the ring.</summary>
    [Export] public bool ShowTargetLine { get; set; } = true;

    [Export] public Color CoreColor { get; set; } = new(0.3f, 0.85f, 1f, 0.5f);
    [Export] public Color AggroColor { get; set; } = new(1f, 0.6f, 0.2f, 0.75f);
    [Export] public Color IdleColor { get; set; } = new(0.6f, 0.6f, 0.65f, 0.35f);

    private const int Segments = 48;

    public override void _Ready() => ZIndex = 90;   // over the ground, under the state readout

    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            if (Visible) { Visible = false; QueueRedraw(); }
            return;
        }

        Visible = true;
        QueueRedraw();   // the enemy moves every frame, so the ring and the line move with it
    }

    public override void _Draw()
    {
        if (Controller?.Targeter is not EnemyStructureTargeter targeter) return;

        Node2D target = targeter.CurrentTarget;
        bool onTurret = target != null && target.IsInGroup("Towers");
        Color color = target == null ? IdleColor : onTurret ? AggroColor : CoreColor;

        // No ring while railroaded: the enemy cannot break off, so a radius would be a lie.
        if (!targeter.Railroaded && targeter.AggroRadius > 0)
            DrawRing(targeter.AggroRadius, color);

        if (!ShowTargetLine || target == null) return;

        // Local space — this node rides the enemy, so the target has to come back to it.
        Vector2 local = ToLocal(target.GlobalPosition);
        DrawLine(Vector2.Zero, local, color, 1f);
        DrawRect(new Rect2(local - new Vector2(3, 3), new Vector2(6, 6)), color, filled: false, width: 1f);
    }

    private void DrawRing(float radius, Color color)
    {
        var points = new Vector2[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * Mathf.Tau / Segments;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        DrawPolyline(points, color, 1f);
    }
}
