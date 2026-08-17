namespace towerdefensegame.scripts.ui;

/// <summary>
/// Every <c>CanvasLayer</c> number in the <b>root</b> viewport, in one place. Assign in
/// <c>_Ready</c>, never in the <c>.tscn</c>.
///
/// <list type="bullet">
///   <item><b>0–99</b> — under <c>Pausable</c>. Game and HUD.</item>
///   <item><b>100+</b> — under <c>WhenPaused</c>. Full-screen modals, which also need the mouse
///     first (higher layer is picked first).</item>
/// </list>
///
/// A <c>SubViewport</c> is its own ordering space, so its layers belong elsewhere, not here —
/// layer 128 inside one still draws under layer 1 out here.
/// </summary>
public static class UiLayer
{
    // ── Pausable: 0–99 ───────────────────────────────────────────────────────────

    /// <summary>Wave countdown and queue size.</summary>
    public const int WaveTimer = 50;

    /// <summary>Fade-to-black while the player spawns. Over the HUD, as it always was.</summary>
    public const int SpawnFade = 60;

    // ── WhenPaused: 100+ ─────────────────────────────────────────────────────────

    /// <summary>The crystal lattice editor, full-screen over a paused game.</summary>
    public const int LatticeEditor = 110;
}
