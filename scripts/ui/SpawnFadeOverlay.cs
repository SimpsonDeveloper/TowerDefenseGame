using Godot;

/// <summary>
/// Full-screen black overlay that fades out when the player finishes spawning.
/// Listens for the PlayerController.Spawned signal — no game logic lives here.
/// </summary>
public partial class SpawnFadeOverlay : CanvasLayer
{
	[Export] public bool ShowUi { get; set; } = true;
	[Export] public float FadeDuration { get; set; } = 0.6f;
	[Export] public float FadeDelay { get; set; } = 0.5f;
	[Export] public PlayerController Player { get; set; }

	private ColorRect _fadeRect;

	public override void _Ready()
	{
		if (!ShowUi)
		{
			QueueFree();
			return;
		}

		Layer = 100;

		_fadeRect = new ColorRect();
		_fadeRect.Color = new Color(0, 0, 0, 1);
		_fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_fadeRect);

		if (Player != null)
			Player.Spawned += OnPlayerSpawned;
	}

	private async void OnPlayerSpawned()
	{
		await ToSignal(GetTree().CreateTimer(FadeDelay), SceneTreeTimer.SignalName.Timeout);
		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "color:a", 0.0f, FadeDuration)
			 .SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
