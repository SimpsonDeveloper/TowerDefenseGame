using Godot;

namespace towerdefensegame;

[GlobalClass]
public partial class GenRange : Resource
{
    [Export]
    public int FirstIndex;
    [Export]
    public int LastIndex;
}