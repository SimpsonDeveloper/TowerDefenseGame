using Godot;
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
/// and an empty one is permanently railroaded.
/// </summary>
[GlobalClass]
public partial class MindBarComponent : StatBarComponent
{
    [Export] public MindComponent Mind;

    /// <summary>Purple, set on the type rather than in each scene.</summary>
    public MindBarComponent()
    {
        Size = new Vector2(32f, 3f);
        Offset = new Vector2(0f, -46f);
        FillColor = new Color("a974ff");
    }

    public override void _Ready()
    {
        base._Ready();

        if (Mind == null)
        {
            GD.PushWarning($"{Name}: Mind not assigned — the Mind bar will stay hidden.");
            return;
        }

        Mind.Changed += OnChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Mind != null) Mind.Changed -= OnChanged;
    }

    protected override bool TryGetFraction(out float fraction)
    {
        fraction = 0f;

        if (Mind == null || Mind.MaxMind <= 0) return false;

        fraction = Mathf.Clamp((float)(Mind.Mind / Mind.MaxMind), 0f, 1f);
        return true;
    }

    private void OnChanged(double mind) => Refresh();
}
