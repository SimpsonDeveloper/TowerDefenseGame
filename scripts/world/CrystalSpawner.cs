using Godot;
using towerdefensegame.scripts.components;
using towerdefensegame.scripts.player;
using towerdefensegame.scripts.world.resources;

namespace towerdefensegame.scripts.world;
/// <summary>
/// Spawns a ring of crystals around the player when <see cref="OnPlayerSpawned"/>
/// is called. Connect <see cref="PlayerSpawner.PlayerSpawned"/> to this method in
/// the scene. Each spawned crystal is assigned a random <see cref="ResourceData"/>
/// from the <see cref="Variants"/> array.
/// </summary>
public partial class CrystalSpawner : Node2D
{
    /// <summary>The harvestable crystal prefab scene to instantiate.</summary>
    [Export] public PackedScene CrystalScene { get; set; }

    /// <summary>Total number of crystals to place around the player.</summary>
    [Export] public int SpawnCount { get; set; } = 8;

    /// <summary>Approximate distance from the player in pixels.</summary>
    [Export] public float SpawnRadius { get; set; } = 300f;

    /// <summary>
    /// Available crystal resource variants. Each spawned crystal picks one at random.
    /// Leave empty to use the scene's default textures.
    /// </summary>
    [Export] public ResourceData[] Variants { get; set; } = [];

    private RandomNumberGenerator _rng = new();

    /// <summary>
    /// Called via the PlayerSpawner.PlayerSpawned signal. Spawns crystals around
    /// the player's position.
    /// </summary>
    public void OnPlayerSpawned(PlayerController player)
    {
        if (ProcessMode == ProcessModeEnum.Disabled) return;

        if (CrystalScene == null)
        {
            GD.PushWarning("CrystalSpawner: CrystalScene export is not set.");
            return;
        }

        _rng.Randomize();

        for (int i = 0; i < SpawnCount; i++)
        {
            // Spread evenly around a ring with slight random jitter.
            float angle  = i * Mathf.Tau / SpawnCount + _rng.RandfRange(-0.25f, 0.25f);
            float radius = SpawnRadius + _rng.RandfRange(-40f, 40f);
            var   pos    = player.GlobalPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            var crystal = CrystalScene.Instantiate<Node2D>();

            if (Variants.Length > 0)
            {
                var variant = Variants[_rng.RandiRange(0, Variants.Length - 1)];
                ApplyVariant(crystal, variant);
            }

            GetParent().AddChild(crystal);
            crystal.GlobalPosition = pos;
        }
    }

    private static void ApplyVariant(Node2D crystal, ResourceData data)
    {
        bool harvestTextureApplied = false;
        bool dropDataApplied = false;
        foreach (var child in crystal.GetChildren())
        {
            if (child is SpriteComponent spriteComponent)
            {
                spriteComponent.Texture = data.HarvestableTexture;
                harvestTextureApplied = true;
            }
            else if (child is DropSpawner spawner)
            {
                spawner.ResourceId = data.ResourceId;
                dropDataApplied = true;
            }
            else if (harvestTextureApplied && dropDataApplied)
            {
                return;
            }
        }
    }
}
