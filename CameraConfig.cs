using Godot;

public partial class CameraConfig : Container
{
	[Export]
	public CameraController Camera { get; set; }

	private Label _smoothSpeedLabel;
	private Label _minZoomLabel;
	private Label _maxZoomLabel;

	public override void _Ready()
	{
		if (Camera == null)
		{
			QueueFree();
			return;
		}

		CreateSliders();
	}

	private void CreateSliders()
	{
		_smoothSpeedLabel = SliderBuilder.AddSlider(this, "Smooth Speed", 1f, 20f, 0.5f, Camera.SmoothSpeed, 0, 0, OnSmoothSpeedChanged);
		_minZoomLabel     = SliderBuilder.AddSlider(this, "Min Zoom",     0.1f, 2.0f, 0.1f, Camera.MinZoom,     1, 0, OnMinZoomChanged);
		_maxZoomLabel     = SliderBuilder.AddSlider(this, "Max Zoom",     0.5f, 5.0f, 0.1f, Camera.MaxZoom,     1, 1, OnMaxZoomChanged);
	}

	private void OnSmoothSpeedChanged(double value)
	{
		Camera.SmoothSpeed = (float)value;
		_smoothSpeedLabel.Text = SliderBuilder.FormatLabel("Smooth Speed", (float)value);
	}

	private void OnMinZoomChanged(double value)
	{
		Camera.MinZoom = (float)value;
		_minZoomLabel.Text = SliderBuilder.FormatLabel("Min Zoom", (float)value);
	}

	private void OnMaxZoomChanged(double value)
	{
		Camera.MaxZoom = (float)value;
		_maxZoomLabel.Text = SliderBuilder.FormatLabel("Max Zoom", (float)value);
	}
}
