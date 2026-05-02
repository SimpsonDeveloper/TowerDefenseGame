using System;
using Godot;

namespace towerdefensegame.scripts.towers;

/// <summary>
/// Implemented by tower scene roots so the placement system can configure them
/// from a <see cref="TowerDef"/>, and so anything (UI, enemy attacks, scripted
/// events) can request their destruction through a uniform hook.
/// </summary>
public interface ITowerPlaceable
{
    /// <summary>Pre-tree configuration: copy any per-instance values from the def.</summary>
    void Configure(TowerDef def);

    /// <summary>Fires when <see cref="Destroy"/> runs, before <c>QueueFree</c>.
    /// <see cref="TowerPlacementManager"/> subscribes to this on placement so it
    /// can release the footprint and fan out the world-level
    /// <c>TowerRemoved</c> signal regardless of who initiated destruction.</summary>
    event Action<Node2D> Destroyed;

    /// <summary>Tear the tower down: fire <see cref="Destroyed"/>, then free
    /// the node. Implementations may also play death effects here.</summary>
    void Destroy();
}
