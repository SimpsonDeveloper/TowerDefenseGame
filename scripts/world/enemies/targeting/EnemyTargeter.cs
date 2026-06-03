using System;
using Godot;

namespace towerdefensegame.scripts.world.enemies.targeting;

/// <summary>
/// Base for an enemy's targeting strategy — the pluggable "what do I walk
/// toward" half of an enemy. A targeter decides on a destination and emits it;
/// it never touches movement state. The owning <see cref="EnemyNavController"/>
/// drives <see cref="Tick"/> once per physics step and subscribes to the events
/// to steer the body.
///
/// Concrete patterns (e.g. <see cref="EnemyTowerTargeter"/>) subclass this. A
/// <see cref="TargetingPattern"/> resource builds the concrete node at spawn
/// time, so <see cref="EnemyType"/> can swap strategies without the controller
/// knowing which one it got.
/// </summary>
public abstract partial class EnemyTargeter : Node
{
    /// <summary>Fires when a fresh, navmesh-validated destination is ready.</summary>
    public event Action<Vector2> ApproachResolved;

    /// <summary>Fires when the current target became invalid or none is reachable.</summary>
    public event Action TargetCleared;

    /// <summary>The thing currently being approached, or null.</summary>
    public Node2D CurrentTarget { get; protected set; }
    
    /// <summary>Seconds between retargets. Keep above 0.1s.</summary>
    protected static float TargetUpdateInterval => 0.25f;

    /// <summary>Max age (ms) of a path-resolve result before it's discarded.</summary>
    protected static int MaxResultAgeMs => 500;

    /// <summary>
    /// Advance the strategy. Driven by the controller in physics order so a
    /// result that lands this tick is applied before the mover consumes it.
    /// </summary>
    public abstract void Tick(double delta);

    protected void EmitApproachResolved(Vector2 destination) => ApproachResolved?.Invoke(destination);
    protected void EmitTargetCleared() => TargetCleared?.Invoke();
}
