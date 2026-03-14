using Godot;

/// <summary>
/// Spawns a ring of crystals around the player after they have found a valid spawn
/// position. Supports multiple crystal types defined by color; each spawned crystal
/// is assigned a random color from the list.
/// </summary>
public partial class CrystalSpawner : Node
{
    /// <summary>The harvestable crystal prefab scene to instantiate.</summary>
    [Export] public PackedScene CrystalScene { get; set; }

    [Export] public PlayerController Player { get; set; }

    /// <summary>Total number of crystals to place around the player.</summary>
    [Export] public int SpawnCount { get; set; } = 8;

    /// <summary>Approximate distance from the player in pixels.</summary>
    [Export] public float SpawnRadius { get; set; } = 300f;

    /// <summary>
    /// One color per crystal type. Each spawned crystal picks one at random.
    /// Leave empty to use the sprite's default colors.
    /// </summary>
    [Export] public Color[] CrystalColors { get; set; } = [];

    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        if (Player == null)
        {
            GD.PushWarning("CrystalSpawner: Player export is not set.");
            return;
        }

        Player.Spawned += OnPlayerSpawned;
    }

    private void OnPlayerSpawned()
    {
        _rng.Randomize();

        if (CrystalScene == null)
        {
            GD.PushWarning("CrystalSpawner: CrystalScene export is not set.");
            return;
        }

        for (int i = 0; i < SpawnCount; i++)
        {
            // Spread evenly around a ring with slight random jitter.
            float angle  = i * Mathf.Tau / SpawnCount + _rng.RandfRange(-0.25f, 0.25f);
            float radius = SpawnRadius + _rng.RandfRange(-40f, 40f);
            var   pos    = Player.GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            var crystal = CrystalScene.Instantiate<Harvestable>();

            if (CrystalColors.Length > 0)
            {
                var color = CrystalColors[_rng.RandiRange(0, CrystalColors.Length - 1)];
                crystal.GetNode<SpriteComponent>("Sprite2D").Modulate = color;
            }

            GetTree().CurrentScene.AddChild(crystal);
            crystal.GlobalPosition = pos;
        }
    }
}
