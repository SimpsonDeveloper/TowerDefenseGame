using System;
using Godot;
using towerdefensegame.scripts.components;

namespace towerdefensegame.scripts.towers;

public partial class TurretTower : StaticBody2D, ITowerPlaceable
{
	[Export] public SpriteComponent TurretSprite;
	[Export] public DetectionZone TargetingZone;
	[Export] public CollisionShape2D TargetingZoneCollisionShape;
	[Export] public HealthComponent Health;
	[Export] public Node2D Laser;
	[Export] public Line2D LaserLine;
	[Export] public GpuParticles2D ShootParticles;
	[Export] public GpuParticles2D HitParticles;
	[Export] public float RotationSpeed = 8;
	[Export] public float AimToleranceDeg = 5f;
	[Export] public float LaserVisibleDuration = 0.08f;

	private Node2D _target;
	private float _targetRadius;
	private int _damage;
	private float _fireInterval;
	private float _aimToleranceRad;
	private float _fireCooldown;
	private float _laserVisibleTimer;
	private float _muzzleOffset;
	private ShaderMaterial _laserMaterial;
	private const float LaserStartIntensity = 0.874f;
	private TowerFootprintTracker _footprints;

	public event Action<Node2D> Destroyed;

	// Stores stats before entering the tree; nodes resolve in _Ready.
	public void Configure(TowerDef def)
	{
		_targetRadius = def.TargetRadius;
		_damage = def.Damage;
		_fireInterval = def.FireInterval;
	}

	public override void _Ready()
	{
		AddToGroup("Towers");
		_aimToleranceRad = Mathf.DegToRad(AimToleranceDeg);
		if (TargetingZoneCollisionShape?.Shape is CircleShape2D circle)
			circle.Radius = _targetRadius;

		// Laser sits at (muzzleOffset, 0) in TurretSprite-local space; cache the
		// offset so we can shrink the beam by it when computing length-to-enemy.
		if (Laser != null) _muzzleOffset = Laser.Position.X;

		if (LaserLine?.Material is ShaderMaterial mat)
		{
			_laserMaterial = (ShaderMaterial)mat.Duplicate();
			LaserLine.Material = _laserMaterial;
			_laserMaterial.SetShaderParameter("intensity", 0f);
		}

		// Cache so _ExitTree doesn't need a viewport lookup during teardown.
		_footprints = TowerFootprintTracker.ForViewport(GetViewport());

		// Route HP depletion through Destroy so footprint cleanup and
		// ITowerPlaceable.Destroyed fan-out happen the same way as a UI-driven
		// tear-down.
		if (Health != null)
			Health.Destroyed += Destroy;
	}

	/// <summary>Public destruction entry point. Fires <see cref="Destroyed"/> so
	/// <see cref="TowerPlacementManager"/> can release the footprint and fan out
	/// <c>TowerRemoved</c>, then frees the node.</summary>
	public void Destroy()
	{
		// Leave the Towers group before fan-out so the rebake triggered by
		// TowerRemoved doesn't see this body's collision as an obstruction
		// (QueueFree only takes effect at end of frame).
		RemoveFromGroup("Towers");
		Destroyed?.Invoke(this);
		QueueFree();
	}

	public override void _ExitTree()
	{
		// Defensive fallback: if the tower is freed without going through
		// Destroy() (e.g. scene unload), make sure the footprint slot is freed.
		// Idempotent — Unregister no-ops if we already left.
		_footprints?.Unregister(this);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_fireCooldown -= dt;

		if (_laserVisibleTimer > 0f)
		{
			_laserVisibleTimer -= dt;
			if (_laserVisibleTimer <= 0f)
			{
				if (_laserMaterial != null)
					_laserMaterial.SetShaderParameter("intensity", 0f);
				if (LaserLine != null) LaserLine.Visible = false;
			}
			else if (_laserMaterial != null && LaserVisibleDuration > 0f)
			{
				float t = _laserVisibleTimer / LaserVisibleDuration;
				_laserMaterial.SetShaderParameter("intensity", LaserStartIntensity * t);
			}
		}

		_target = FindClosestInZone();
		if (_target == null) return;

		Vector2 directionToTarget = _target.GlobalPosition - TurretSprite.GlobalPosition;
		float targetAngle = directionToTarget.Angle();

		float angleDiff = Mathf.Wrap(targetAngle - TurretSprite.Rotation, -Mathf.Pi, Mathf.Pi);
		float rotationStep = Mathf.Clamp(angleDiff, -RotationSpeed * dt, RotationSpeed * dt);
		TurretSprite.Rotation += rotationStep;

		if (_fireCooldown <= 0f && Mathf.Abs(angleDiff) <= _aimToleranceRad)
			Fire(directionToTarget.Length());
	}

	private void Fire(float distanceToTarget)
	{
		_fireCooldown = _fireInterval;

		foreach (var child in _target.GetChildren())
		{
			if (child is HealthComponent { IsDead: false} h)
			{
				h.TakeDamage(_damage);
				break;
			}
		}

		if (Laser == null) return;

		// Beam runs from the muzzle (Laser origin) out to the enemy along
		// TurretSprite's local +X, since aim tolerance keeps the enemy near
		// that axis. Shorten by the muzzle offset to land at the enemy.
		float beamLength = Mathf.Max(distanceToTarget - _muzzleOffset, 0f);

		if (LaserLine != null)
			LaserLine.SetPointPosition(1, new Vector2(beamLength, 0));
		if (HitParticles != null)
			HitParticles.Position = new Vector2(beamLength, 0);

		// Only the beam is toggled — the Laser node itself stays visible so
		// in-flight particles continue their natural lifetime after the line hides.
		if (LaserLine != null) LaserLine.Visible = true;
		if (_laserMaterial != null)
			_laserMaterial.SetShaderParameter("intensity", LaserStartIntensity);
		_laserVisibleTimer = LaserVisibleDuration;
		if (ShootParticles != null) ShootParticles.Restart();
		if (HitParticles != null) HitParticles.Restart();
	}

	// Returns the closest body in the DetectionZone that belongs to the enemies group.
	private Node2D FindClosestInZone()
	{
		Node2D closest = null;
		float closestDist = float.MaxValue;

		foreach (Node2D body in TargetingZone.GetOverlappingBodies())
		{
			if (!body.IsInGroup("enemies")) continue;
			float dist = GlobalPosition.DistanceTo(body.GlobalPosition);
			if (dist < closestDist)
			{
				closestDist = dist;
				closest = body;
			}
		}

		return closest;
	}
}
