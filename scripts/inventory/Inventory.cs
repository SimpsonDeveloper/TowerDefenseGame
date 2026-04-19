using System.Collections.Generic;
using System.Linq;
using Godot;
using towerdefensegame.scripts.world;

namespace towerdefensegame.scripts.inventory;

/// <summary>
/// Tracks the player's collected items. Items of the same name stack up to MaxStackSize
/// before a new slot is created. Emits InventoryChanged whenever the contents change.
/// </summary>
public partial class Inventory : Node2D
{
    public const int MaxStackSize = 100;

    [Signal]
    public delegate void InventoryChangedEventHandler();

    public class InventoryItemStack
    {
        public ResourceId ResourceId;
        public int          Count;
    }

    private readonly Dictionary<ResourceId, List<InventoryItemStack>> _items = new();

    /// <summary>
    /// Flattens the _items dictionary with deferred execution. Doesn't create an intermediate collection.
    /// </summary>
    public IEnumerable<InventoryItemStack> Items => _items.Values.SelectMany(list => list);

    public void AddItem(ResourceId resourceId)
    {
        // Check for existing item stacks with the same resourceEnumValue
        if (_items.TryGetValue(resourceId, out List<InventoryItemStack> candidateStacks))
        {
            // Find an existing item stack which hasn't reached MaxStackSize
            var existing = candidateStacks.FirstOrDefault(i => i.Count < MaxStackSize);
            if (existing != null)
                // Add to the existing stack
                existing.Count++;
            else
                // Start a new stack
                candidateStacks.Add(new InventoryItemStack { Count = 1, ResourceId = resourceId });
        }
        else
        {
            // Start a new stack
            _items[resourceId] = [ new InventoryItemStack { Count = 1, ResourceId = resourceId } ];
        }

        // Signify the inventory has changed
        EmitSignal(SignalName.InventoryChanged);
    }
}
