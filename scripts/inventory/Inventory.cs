using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks the player's collected items. Items of the same name stack up to MaxStackSize
/// before a new slot is created. Emits InventoryChanged whenever the contents change.
/// </summary>
public partial class Inventory : Node
{
    public const int MaxStackSize = 100;

    [Signal]
    public delegate void InventoryChangedEventHandler();

    public class InventoryItem
    {
        public string Name;
        public int    Count;
    }

    private readonly List<InventoryItem> _items = new();

    public IReadOnlyList<InventoryItem> Items => _items;

    public void AddItem(string name)
    {
        var existing = _items.LastOrDefault(i => i.Name == name && i.Count < MaxStackSize);
        if (existing != null)
        {
            existing.Count++;
        }
        else
        {
            _items.Add(new InventoryItem { Name = name, Count = 1 });
        }

        EmitSignal(SignalName.InventoryChanged);
    }
}
