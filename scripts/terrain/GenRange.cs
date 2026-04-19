using Godot;

namespace towerdefensegame.scripts.terrain;

[GlobalClass]
public partial class GenRange : Resource
{
    [Export] public int FirstIndex;
    [Export] public int LastIndex;
}