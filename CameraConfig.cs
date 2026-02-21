using Godot;

public partial class CameraConfig : Container
{
	[Export]
	public CameraController Camera { get; set; }

	private Label _moveSpeedLabel;
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
		AddSlider("Move Speed", 100f, 2000f, 50f, Camera.MoveSpeed, 0, 0, OnMoveSpeedChanged, out _moveSpeedLabel);
		AddSlider("Min Zoom", 0.1f, 2.0f, 0.1f, Camera.MinZoom, 1, 0, OnMinZoomChanged, out _minZoomLabel);
		AddSlider("Max Zoom", 0.5f, 5.0f, 0.1f, Camera.MaxZoom, 1, 1, OnMaxZoomChanged, out _maxZoomLabel);
	}

	private void AddSlider(string name, float min, float max, float step, float initialValue,
		int row, int col, Range.ValueChangedEventHandler callback, out Label label)
	{
		var slider = new HSlider();
		AddChild(slider);
		slider.Size = new Vector2(200, 16);
		slider.Position = new Vector2(8 + 247 * col, 24 + 53 * row);
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialValue;
		slider.ValueChanged += callback;

		var lbl = new Label();
		slider.AddChild(lbl);
		lbl.Size = new Vector2(120, 23);
		lbl.Position = new Vector2(0, -24);
		lbl.Text = FormatLabel(name, initialValue);
		var labelSettings = new LabelSettings();
		labelSettings.SetFontColor(new Color(0, 0, 0, 1));
		lbl.LabelSettings = labelSettings;

		label = lbl;
	}

	private string FormatLabel(string name, float value) => $"{name}: {value:F1}";

	private void OnMoveSpeedChanged(double value)
	{
		Camera.MoveSpeed = (float)value;
		_moveSpeedLabel.Text = FormatLabel("Move Speed", (float)value);
	}

	private void OnMinZoomChanged(double value)
	{
		Camera.MinZoom = (float)value;
		_minZoomLabel.Text = FormatLabel("Min Zoom", (float)value);
	}

	private void OnMaxZoomChanged(double value)
	{
		Camera.MaxZoom = (float)value;
		_maxZoomLabel.Text = FormatLabel("Max Zoom", (float)value);
	}
}
