using Godot;
using towerdefensegame.scripts.towers.crystal.core;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// Carries a compiled shot from the tower that fired it to the enemy it hit — the one place the
/// compilation system and the combat system touch.
///
/// It is a listener on <c>TurretTower.ShotLanded</c> rather than a call inside the turret on
/// purpose: the turret knows it hit something and nothing more, and every question about what a
/// shot *does* stays on this side of the seam.
/// </summary>
public static class ShotDelivery
{
    /// <summary>
    /// Resolve the shot against the enemy's states. An enemy with no
    /// <see cref="EnemyStateComponent"/> simply takes the gun's own damage and nothing else —
    /// unwired, not broken, which is what keeps this landable one enemy scene at a time.
    /// </summary>
    public static void ToEnemy(CompileResult shot, Node2D enemy)
    {
        if (shot == null || enemy == null) return;

        foreach (Node child in enemy.GetChildren())
        {
            if (child is not EnemyStateComponent states) continue;
            states.Receive(shot);
            return;
        }
    }
}
