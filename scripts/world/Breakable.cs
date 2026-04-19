using System.Collections.Generic;
using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.world;

/// <summary>
/// Component that handles harvesting behaviour: HP, crack shader, shake, and
/// colour-sampled particles. Emits Broken when HP reaches zero, which frees the
/// parent body. Connect Broken → HarvestableResource.OnHarvestableBroken in the scene.
/// </summary>
public partial class Breakable : Node2D
{
    [Export] public int MaxHp { get; set; } = 5;
    [Export] public int ParticlesPerTick { get; set; } = 6;
    [Export] public SpriteComponent Sprite { get; set; }
    [Export] public Shader CrackShader { get; set; }

    [Signal]
    public delegate void BrokenEventHandler();

    private int            _hp;
    private Node2D         _root;
    private Vector2        _spriteOrigin;
    private ShaderMaterial _crackMaterial;
    private Tween          _shakeTween;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _hp   = MaxHp;
        _root = GetParent<Node2D>();
        _spriteOrigin = Sprite.Position;
        _rng.Randomize();

        _crackMaterial = new ShaderMaterial { Shader = CrackShader };
        Sprite.Material = _crackMaterial;
    }

    /// <summary>Called by HarvesterComponent on each harvest tick.</summary>
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
        _shakeTween.TweenProperty(Sprite, "position", _spriteOrigin + new Vector2( amount, 0), stepTime);
        _shakeTween.TweenProperty(Sprite, "position", _spriteOrigin + new Vector2(-amount, 0), stepTime);
        _shakeTween.TweenProperty(Sprite, "position", _spriteOrigin,                           stepTime);
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
        };

        emitter.Finished += emitter.QueueFree;

        _root.GetParent().AddChild(emitter);
        emitter.GlobalPosition = _root.GlobalPosition;
    }

    private List<Color> SampleSpriteColors(int count)
    {
        var spriteImage = Sprite.Texture.GetImage();
        var colors   = new List<Color>();
        int width    = spriteImage.GetWidth();
        int height   = spriteImage.GetHeight();
        int attempts = 0;

        while (colors.Count < count && attempts < count * 20)
        {
            attempts++;
            var color = spriteImage.GetPixel(
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
        _root.QueueFree();
    }
}
