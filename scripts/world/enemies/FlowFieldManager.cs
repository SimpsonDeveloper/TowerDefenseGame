using Godot;
using System.Collections.Generic;

namespace towerdefensegame;

/// <summary>
/// Computes a tile-resolution flow field toward a single target (typically the player).
/// Each tile stores a unit vector pointing in the direction of the shortest walkable
/// path to the target. Enemies sample this dictionary at their tile position — the
/// per-enemy cost is essentially free regardless of how many enemies exist.
///
/// The field is recomputed via BFS whenever the target moves more than
/// <see cref="RecomputeThreshold"/> pixels. BFS cost scales with the number of
/// tiles in <see cref="FieldRadius"/>, not with the number of enemies.
///
/// Add to each world scene and wire ChunkManager. Add to group "flow_field_manager"
/// so enemies can find it. See docs/pathfinding_flow_field.md for full setup.
/// </summary>
[GlobalClass]
public partial class FlowFieldManager : Node
{
    // ── Configuration ─────────────────────────────────────────────────────

    [Export] public ChunkManager ChunkManager { get; set; }

    /// <summary>
    /// If set, the manager will automatically resolve its target from this group
    /// on the first process frame where the target is null. Useful for demo scenes
    /// where the target spawns after the manager. Leave empty to assign via SetTarget().
    /// </summary>
    [Export] public string AutoTargetGroup { get; set; } = "Player";

    /// <summary>Tile size in pixels. Must match the TileMap tile size.</summary>
    [Export] public int TileSize { get; set; } = 16;

    /// <summary>
    /// BFS radius in tiles from the target. Tiles outside this radius have no
    /// entry in the field and enemies will fall back to direct steering.
    /// </summary>
    [Export] public int FieldRadius { get; set; } = 128;

    /// <summary>
    /// Target must move at least this many pixels before the field is recomputed.
    /// Higher values reduce CPU cost but make the field stale on a fast-moving target.
    /// One tile width (16px) is a reasonable default.
    /// </summary>
    [Export] public float RecomputeThreshold { get; set; } = 16f;

    // ── Public read-only state ────────────────────────────────────────────

    /// <summary>
    /// Maps tile coordinate → unit vector pointing toward the target.
    /// Read by EnemyFlowFieldController each physics frame.
    /// </summary>
    public Dictionary<Vector2I, Vector2> Field { get; } = new();

    // ── Internal state ────────────────────────────────────────────────────

    private Node2D _target;
    private Vector2 _lastComputedTargetPos = new Vector2(float.MaxValue, float.MaxValue);

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public override void _Ready()
    {
        AddToGroup("flow_field_manager");
    }

    public override void _Process(double delta)
    {
        if (_target == null)
        {
            if (!string.IsNullOrEmpty(AutoTargetGroup))
            {
                var nodes = GetTree().GetNodesInGroup(AutoTargetGroup);
                if (nodes.Count > 0) SetTarget(nodes[0] as Node2D);
            }
            return;
        }

        if (_target.GlobalPosition.DistanceTo(_lastComputedTargetPos) >= RecomputeThreshold)
            RecomputeField();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Assign the target whose position the flow field points toward.</summary>
    public void SetTarget(Node2D target)
    {
        _target = target;
        if (target != null)
            RecomputeField();
    }

    /// <summary>
    /// Sample the flow field at a world position. Returns a unit vector toward
    /// the target, or Vector2.Zero if the position is outside the field.
    /// </summary>
    public Vector2 Sample(Vector2 worldPos)
    {
        Vector2I tile = WorldToTile(worldPos);
        return Field.TryGetValue(tile, out Vector2 dir) ? dir : Vector2.Zero;
    }

    // ── BFS field computation ─────────────────────────────────────────────

    private void RecomputeField()
    {
        if (_target == null || ChunkManager == null) return;

        _lastComputedTargetPos = _target.GlobalPosition;
        Field.Clear();

        Vector2I origin = WorldToTile(_target.GlobalPosition);

        // BFS: propagate outward from the target tile.
        // parent[tile] = the neighbour that is one step closer to the target.
        var parent = new Dictionary<Vector2I, Vector2I>();
        var queue  = new Queue<Vector2I>();

        parent[origin] = origin;
        queue.Enqueue(origin);

        // Cardinal neighbours only (diagonal costs more and is less predictable)
        var offsets = new Vector2I[]
        {
            new( 0, -1), // N
            new( 1,  0), // E
            new( 0,  1), // S
            new(-1,  0), // W
        };

        while (queue.Count > 0)
        {
            Vector2I current = queue.Dequeue();

            // Stop expanding beyond the radius
            if (Mathf.Abs(current.X - origin.X) > FieldRadius ||
                Mathf.Abs(current.Y - origin.Y) > FieldRadius)
                continue;

            foreach (var offset in offsets)
            {
                Vector2I neighbour = current + offset;
                if (parent.ContainsKey(neighbour)) continue;

                // Skip solid tiles; treat unloaded chunks as passable
                TerrainType? terrain = ChunkManager.GetTerrainTypeAtWorldPos(TileToWorld(neighbour));
                if (terrain.HasValue && TerrainTypeExtensions.HasCollision(terrain.Value)) continue;

                parent[neighbour] = current;
                queue.Enqueue(neighbour);
            }
        }

        // Convert parent map into direction vectors.
        // Each tile's vector points from itself toward its parent (one step closer to target).
        foreach (var kvp in parent)
        {
            if (kvp.Key == origin)
            {
                Field[kvp.Key] = Vector2.Zero; // Already at target
                continue;
            }
            Vector2 dir = (TileToWorld(kvp.Value) - TileToWorld(kvp.Key)).Normalized();
            Field[kvp.Key] = dir;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Vector2I WorldToTile(Vector2 worldPos) =>
        new Vector2I(
            Mathf.FloorToInt(worldPos.X / TileSize),
            Mathf.FloorToInt(worldPos.Y / TileSize)
        );

    private Vector2 TileToWorld(Vector2I tile) =>
        new Vector2(tile.X * TileSize + TileSize * 0.5f, tile.Y * TileSize + TileSize * 0.5f);
}
