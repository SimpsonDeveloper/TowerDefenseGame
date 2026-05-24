using Godot;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Drives the spawn_portal shader on a MeshInstance2D: holds a steady idle
/// glow, eases <see cref="intensity"/> toward its target each frame, and can
/// <see cref="Flare"/> for a bright burst when a wave actually fires.
///
/// The material is duplicated on ready so multiple portals animate independently
/// and each gets a random phase, avoiding lockstep spin between neighbours.
/// </summary>
[GlobalClass]
public partial class SpawnerPortal : MeshInstance2D
{
    /// <summary>Resting energy when idle (0 = invisible, 1 = full).</summary>
    [Export] public float IdleIntensity { get; set; } = 0.7f;

    /// <summary>Peak energy reached right after <see cref="Flare"/>.</summary>
    [Export] public float FlareIntensity { get; set; } = 1.6f;

    /// <summary>Seconds for a flare to decay back to idle.</summary>
    [Export] public float FlareDecay { get; set; } = 0.8f;

    /// <summary>How quickly intensity chases its target (higher = snappier).</summary>
    [Export] public float Responsiveness { get; set; } = 6f;

    private ShaderMaterial _mat;
    private float _intensity;
    private float _flare; // 0..1, decays to 0

    public override void _Ready()
    {
        if (Material is not ShaderMaterial src)
        {
            GD.PushWarning($"{Name}: no ShaderMaterial on Material — portal won't animate.");
            return;
        }

        // Own copy so each portal animates independently of others sharing the scene's material.
        _mat = (ShaderMaterial)src.Duplicate();
        Material = _mat;

        _mat.SetShaderParameter("time_offset", GD.Randf() * 100f);
        _intensity = IdleIntensity;
        _mat.SetShaderParameter("intensity", _intensity);
    }

    public override void _Process(double delta)
    {
        if (_mat == null) return;

        _flare = Mathf.MoveToward(_flare, 0f, (float)delta / Mathf.Max(FlareDecay, 0.001f));
        float target = IdleIntensity + _flare * (FlareIntensity - IdleIntensity);
        _intensity = Mathf.Lerp(_intensity, target, 1f - Mathf.Exp(-Responsiveness * (float)delta));
        _mat.SetShaderParameter("intensity", _intensity);
    }

    /// <summary>Punch the portal to full brightness; it decays back over <see cref="FlareDecay"/> seconds.</summary>
    public void Flare() => _flare = 1f;

    /// <summary>
    /// Close the portal: stop emitting motes, ease the glow to dark, and free
    /// the node after <paramref name="fadeSeconds"/>. The shader fade is driven
    /// by <see cref="Responsiveness"/> via the IdleIntensity target reaching 0.
    /// </summary>
    public void Close(float fadeSeconds = 1f)
    {
        IdleIntensity = 0f;
        _flare = 0f;
        if (GetNodeOrNull<GpuParticles2D>("Motes") is { } motes)
            motes.Emitting = false;
        GetTree().CreateTimer(fadeSeconds).Timeout += QueueFree;
    }
}