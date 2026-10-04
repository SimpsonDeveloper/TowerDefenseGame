using Godot;
using towerdefensegame.scripts.combat.core;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// The enemy's <b>Mind</b> meter — the second bar, drained by mind-damage
/// (<c>effect-vocab/vocab-overview/illusion.md</c>).
///
/// Purple, matching Amethyst, the crystal that drains it. Matched by hand: the lattice draws its
/// crystals from <c>CrystalVisuals.Tint</c> and the editor's buttons from a theme, and neither is
/// reachable from here, so the three are kept in step by eye.
///
/// <b>Worth knowing that this bar reads the opposite way round to the others.</b> A full Mind is
/// the dangerous case — the enemy is most likely to see through the maze and break off the path —
/// and an empty one is permanently railroaded. It hides at full anyway, like the HP bar: the bars
/// behaving alike won over flagging the threat.
/// </summary>
[GlobalClass]
public partial class MindBarComponent : StatBarComponent
{
    [Export] public EnemyStateComponent States;

    public override void _Ready()
    {
        base._Ready();

        if (States == null)
        {
            GD.PushWarning($"{Name}: States not assigned — the Mind bar will stay hidden.");
            return;
        }

        // Safe in _Ready whatever order the siblings run in: EnemyStateComponent builds its state
        // on construction, and assigning its vitals later raises MindChanged in its own right.
        States.State.MindChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (States != null) States.State.MindChanged -= Refresh;
    }

    protected override bool TryGetFraction(out float fraction)
    {
        fraction = 0f;

        EnemyState state = States?.State;
        if (state == null || state.Vitals.MaxMind <= 0) return false;

        fraction = Mathf.Clamp((float)(state.Mind / state.Vitals.MaxMind), 0f, 1f);
        return true;
    }
}
