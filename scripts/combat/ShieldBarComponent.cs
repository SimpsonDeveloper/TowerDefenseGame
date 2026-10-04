using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// The enemy's <b>Shield</b> — the layer in front of HP
/// (<c>effect-vocab/vocab-overview/shield.md</c>). Light blue, above the Mind bar.
///
/// Hidden entirely on a unit with no shield, which is most of them: <see cref="TryGetFraction"/>
/// reports nothing rather than drawing an empty bar. That is the same mechanism that will hide it
/// mid-wave if shields ever become conditional.
/// </summary>
[GlobalClass]
public partial class ShieldBarComponent : StatBarComponent
{
    [Export] public ShieldComponent Shield;

    /// <summary>Light blue, set on the type rather than in each scene.</summary>
    public ShieldBarComponent()
    {
        Size = new Vector2(32f, 3f);
        Offset = new Vector2(0f, -52f);
        FillColor = new Color("6fd3ff");
    }

    public override void _Ready()
    {
        base._Ready();

        if (Shield == null) return;   // no shield is the normal case, not a misconfiguration

        Shield.Changed += OnChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (Shield != null) Shield.Changed -= OnChanged;
    }

    protected override bool TryGetFraction(out float fraction)
    {
        fraction = 0f;

        if (Shield == null || !Shield.HasShield) return false;

        fraction = Mathf.Clamp((float)(Shield.Shield / Shield.MaxShield), 0f, 1f);
        return true;
    }

    private void OnChanged(double shield) => Refresh();
}
