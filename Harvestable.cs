using System.Collections.Generic;
using Godot;

/// <summary>
/// A StaticBody2D that can be harvested by a HarvestComponent.
/// Each harvest tick applies a crack shader step, a shake, and color-sampled particles.
/// When HP reaches zero the node frees itself; particles linger independently because
/// each burst is a fresh GpuParticles2D added directly to the scene root.
/// </summary>
public partial class Harvestable : StaticBody2D
{
    /// <summary>Number of harvest ticks before this node breaks.</summary>
    [Export]
    public int MaxHp { get; set; } = 5;

    /// <summary>Particles emitted per tick. Doubled on the final breaking tick.</summary>
    [Export]
    public int ParticlesPerTick { get; set; } = 6;

    [Signal]
    public delegate void BrokenEventHandler();

    private int             _hp;
    private Sprite2D        _sprite;
    private Vector2         _spriteOrigin;
    private ShaderMaterial  _crackMaterial;
    private Image           _spriteImage;
    private Tween           _shakeTween;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _hp     = MaxHp;
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _spriteOrigin = _sprite.Position;
        _rng.Randomize();

        var shader = GD.Load<Shader>("res://crystal_crack.gdshader");
        _crackMaterial = new ShaderMaterial { Shader = shader };
        _sprite.Material = _crackMaterial;

        _spriteImage = _sprite.Texture.GetImage();
    }

    /// <summary>Called by HarvestComponent on each harvest tick.</summary>
    public void ApplyHarvestTick()
    {
        _hp = Mathf.Max(_hp - 1, 0);

        UpdateCrackShader();
        TriggerShake();
        SpawnParticles(ParticlesPerTick);

        if (_hp == 0)
            Break();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void UpdateCrackShader()
    {
        float progress = 1f - (float)_hp / MaxHp;
        _crackMaterial.SetShaderParameter("crack_progress", progress);
    }

    private void TriggerShake()
    {
        _shakeTween?.Kill();

        float amount   = 3f;
        float stepTime = 0.04f;

        _shakeTween = CreateTween();
        _shakeTween.TweenProperty(_sprite, "position", _spriteOrigin + new Vector2( amount, 0), stepTime);
        _shakeTween.TweenProperty(_sprite, "position", _spriteOrigin + new Vector2(-amount, 0), stepTime);
        _shakeTween.TweenProperty(_sprite, "position", _spriteOrigin,                           stepTime);
    }

    private void SpawnParticles(int count)
    {
        var colors = SampleSpriteColors(count);
        if (colors.Count == 0)
            return;

        if (colors.Count == 1)
            colors.Add(colors[0]);

        var gradient   = new Gradient();
        var offsets    = new float[colors.Count];
        var gradColors = new Color[colors.Count];
        for (int i = 0; i < colors.Count; i++)
        {
            offsets[i]    = (float)i / (colors.Count - 1);
            gradColors[i] = colors[i];
        }
        gradient.Offsets = offsets;
        gradient.Colors  = gradColors;

        var material = new ParticleProcessMaterial
        {
            ParticleFlagDisableZ = true,
            Direction            = new Vector3(0f, -1f, 0f),
            Spread               = 150f,
            InitialVelocityMin   = 40f,
            InitialVelocityMax   = 120f,
            Gravity              = new Vector3(0f, 260f, 0f),
            ScaleMin             = 2f,
            ScaleMax             = 4f,
            ColorInitialRamp     = new GradientTexture1D { Gradient = gradient },
        };

        var emitter = new GpuParticles2D
        {
            OneShot         = true,
            Emitting        = true,
            Amount          = count,
            Lifetime        = 0.5,
            Explosiveness   = 1.0f,
            LocalCoords     = false,
            ProcessMaterial = material,
            ZIndex          = 2,
        };

        emitter.Finished += emitter.QueueFree;

        // Parent directly to the scene root so bursts outlive the crystal node
        GetTree().CurrentScene.AddChild(emitter);
        emitter.GlobalPosition = GlobalPosition;
    }

    private List<Color> SampleSpriteColors(int count)
    {
        var colors   = new List<Color>();
        int width    = _spriteImage.GetWidth();
        int height   = _spriteImage.GetHeight();
        int attempts = 0;

        while (colors.Count < count && attempts < count * 20)
        {
            attempts++;
            var color = _spriteImage.GetPixel(
                _rng.RandiRange(0, width  - 1),
                _rng.RandiRange(0, height - 1));

            if (color.A > 0.1f)
                colors.Add(color);
        }

        return colors;
    }

    private void Break()
    {
        SpawnParticles(ParticlesPerTick * 2);
        EmitSignal(SignalName.Broken);
        QueueFree();
    }
}
