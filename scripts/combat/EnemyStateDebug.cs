using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.combat.core;

namespace towerdefensegame.scripts.combat;

/// <summary>
/// Prints an enemy's carried states above its HP bar, and flashes each line as its op ticks.
/// A development readout, not a game visual: turn it off with <see cref="Enabled"/> and nothing
/// about combat changes.
///
/// It exists because states are otherwise invisible. Burn is a number in a dictionary bleeding a
/// fraction of a point a second — without this, the only evidence it works at all is an HP bar
/// moving slightly faster than expected.
///
/// Drawn immediate-mode like <c>HealthBarComponent</c>, deliberately: this is a <c>Node2D</c>, not
/// a <c>Control</c>, so nothing here touches the UI theme or has any opinion about it.
/// </summary>
[GlobalClass]
public partial class EnemyStateDebug : Node2D
{
    [Export] public EnemyStateComponent States;

    /// <summary>Local offset from the owner. Negative Y places the readout above.</summary>
    [Export] public Vector2 Offset { get; set; } = new(0f, -48f);

    [Export] public int FontSize { get; set; } = 9;

    /// <summary>Off means not drawn and not subscribed — no cost at all.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>Seconds a line stays highlighted after its op ticks.</summary>
    [Export] public float FlashDuration { get; set; } = 0.25f;

    [Export] public Color TextColor { get; set; } = new(0.85f, 0.87f, 0.92f);
    [Export] public Color FlashColor { get; set; } = new(1f, 0.72f, 0.35f);
    [Export] public Color BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.55f);

    /// <summary>Seconds of flash left per state, keyed the same way the states themselves are.</summary>
    private readonly Dictionary<StateId, float> _flash = new();

    private EnemyState _subscribed;

    public override void _Ready()
    {
        Position = Offset;
        ZIndex = 100;   // over the sprite and the bar, since it is an overlay

        if (States == null)
            GD.PushWarning($"{Name}: States not assigned — nothing to report.");
    }

    public override void _ExitTree() => Unsubscribe();

    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            Unsubscribe();
            if (Visible) { Visible = false; QueueRedraw(); }
            return;
        }

        Visible = true;

        // Subscribed here rather than in _Ready: EnemyState is built in the other component's
        // _Ready, and sibling order decides which of the two runs first.
        if (_subscribed == null && States?.State != null)
        {
            _subscribed = States.State;
            _subscribed.Ticked += OnTicked;
        }

        DecayFlashes((float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (States?.State == null) return;

        List<string> lines = new();
        List<StateId> owners = new();

        foreach (StateId state in States.State.ActiveStates)
        {
            lines.Add(Describe(States.State, state));
            owners.Add(state);
        }

        if (States.DotDamageDealt > 0)
        {
            lines.Add($"dot {States.DotDamageDealt:0.#}");
            owners.Add(StateId.None);   // no state owns the total, so it never flashes
        }

        if (lines.Count == 0) return;

        Font font = ThemeDB.FallbackFont;
        float lineHeight = FontSize + 3f;

        // Stacked upward from the offset so the first state sits nearest the bar and the block
        // grows away from the enemy instead of over it.
        float top = -lineHeight * lines.Count;
        float width = 0f;
        foreach (string line in lines)
            width = Mathf.Max(width, font.GetStringSize(line, fontSize: FontSize).X);

        DrawRect(new Rect2(-2f, top - 2f, width + 4f, lineHeight * lines.Count + 4f), BackgroundColor);

        for (int i = 0; i < lines.Count; i++)
        {
            Color color = _flash.ContainsKey(owners[i]) ? FlashColor : TextColor;
            DrawString(font, new Vector2(0f, top + lineHeight * (i + 1) - 3f), lines[i],
                HorizontalAlignment.Left, -1, FontSize, color);
        }
    }

    /// <summary>One line per state: stacks or seconds left, plus its countdown to the next tick.</summary>
    private static string Describe(EnemyState enemy, StateId state)
    {
        int stacks = enemy.Stacks(state);
        string held = stacks > 0 ? $"x{stacks}" : $"{enemy.FlagTimeLeft(state):0.0}s";

        double due = enemy.TimeToNextTick(state);
        return due > 0 ? $"{state} {held}  ({due:0.0})" : $"{state} {held}";
    }

    private void DecayFlashes(float delta)
    {
        if (_flash.Count == 0) return;

        foreach (StateId state in new List<StateId>(_flash.Keys))
        {
            float left = _flash[state] - delta;
            if (left > 0) _flash[state] = left;
            else _flash.Remove(state);
        }
    }

    private void OnTicked(StateId state) => _flash[state] = FlashDuration;

    private void Unsubscribe()
    {
        if (_subscribed == null) return;
        _subscribed.Ticked -= OnTicked;
        _subscribed = null;
    }
}
