using Godot;

/// <summary>
/// A typed Area2D used as a detection zone for overlap-based actions such as
/// harvesting and picking up items. Attach a CollisionShape2D child to define
/// the detection area. Has no physical presence — does not block movement.
/// </summary>
public partial class DetectionZone : Area2D
{
}
