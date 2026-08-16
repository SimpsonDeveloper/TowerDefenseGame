namespace towerdefensegame.scripts.ui;

/// <summary>
/// Draw order for the root viewport's <c>CanvasLayer</c>s, in one place.
///
/// Godot has no sublayers: <c>CanvasLayer.layer</c> is a single absolute int per viewport
/// (-128…128), and nesting layers does not compose — an inner layer's number is measured against
/// every other layer in the viewport, not against its parent. So the only thing keeping two
/// overlays from silently fighting is a convention, which is this file.
///
/// The bands mirror the scene's two halves (`main_scene_raycast_agent.tscn`):
///
/// <list type="bullet">
///   <item><b>0–99</b> — under <c>Pausable</c>. The game and its HUD.</item>
///   <item><b>100+</b> — under <c>WhenPaused</c>. Modals that take over the screen: they have to
///     out-draw everything below <i>and</i> get the mouse first, and a higher layer is picked
///     first.</item>
/// </list>
///
/// Every one of these is assigned in <c>_Ready</c>, never in the <c>.tscn</c> — a number in the
/// scene would just be overwritten at load, so this file is the only place to read or change one.
///
/// These numbers describe the <b>root</b> viewport only. Each <c>SubViewport</c> is its own
/// independent ordering space, composited into its parent as a single Control, so a layer of 128
/// inside the pocket dimension still draws under a layer of 1 out here. <c>TowerPlacementUI</c>'s
/// layer lives in that other space and is deliberately absent below.
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
