using Godot;

/// <summary>
/// Full-screen black overlay that fades out when the player finishes spawning.
/// Connect <see cref="PlayerSpawner.PlayerSpawned"/> to <see cref="OnPlayerSpawned"/>.
/// </summary>
public partial class SpawnFadeOverlay : CanvasLayer
{
	[Export] public bool ShowUi { get; set; } = true;
	[Export] public float FadeDuration { get; set; } = 0.6f;
	[Export] public float FadeDelay { get; set; } = 0.5f;

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
	}

	/// <summary>Called via the PlayerSpawner.PlayerSpawned signal.</summary>
	public async void OnPlayerSpawned(PlayerController _)
	{
		await ToSignal(GetTree().CreateTimer(FadeDelay), SceneTreeTimer.SignalName.Timeout);
		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "color:a", 0.0f, FadeDuration)
			 .SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
