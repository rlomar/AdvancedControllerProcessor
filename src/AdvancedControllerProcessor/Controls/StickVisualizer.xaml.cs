using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdvancedControllerProcessor.Controls;

/// <summary>
/// Canvas-based stick position visualizer.
/// Shows raw and processed stick positions with crosshair and deadzone indicator.
/// </summary>
public partial class StickVisualizer : UserControl
{
    private const float CanvasSize = 200f;
    private const float DotRadius = 6f;

    public StickVisualizer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Update the visualizer with new stick data.
    /// </summary>
    /// <param name="rawX">Raw X position [-1, 1].</param>
    /// <param name="rawY">Raw Y position [-1, 1].</param>
    /// <param name="processedX">Processed X position [-1, 1].</param>
    /// <param name="processedY">Processed Y position [-1, 1].</param>
    /// <param name="deadzone">Deadzone radius [0, 0.5].</param>
    public void UpdatePosition(float rawX, float rawY, float processedX, float processedY, float deadzone = 0f)
    {
        // Convert normalized coordinates to canvas pixels
        // (0,0) in normalized = center of canvas (100, 100)
        // +X = right, +Y = down (matches DualSense convention)

        float rawCanvasX = (rawX + 1f) / 2f * CanvasSize;
        float rawCanvasY = (rawY + 1f) / 2f * CanvasSize;
        float processedCanvasX = (processedX + 1f) / 2f * CanvasSize;
        float processedCanvasY = (processedY + 1f) / 2f * CanvasSize;

        // Position raw dot (centered on coordinates)
        Canvas.SetLeft(RawDot, rawCanvasX - DotRadius);
        Canvas.SetTop(RawDot, rawCanvasY - DotRadius);

        // Position processed dot
        Canvas.SetLeft(ProcessedDot, processedCanvasX - 7);
        Canvas.SetTop(ProcessedDot, processedCanvasY - 7);

        // Update deadzone circle
        float deadzonePixelRadius = deadzone * CanvasSize;
        float deadzoneDiameter = deadzonePixelRadius * 2;
        DeadzoneCircle.Width = deadzoneDiameter;
        DeadzoneCircle.Height = deadzoneDiameter;
        Canvas.SetLeft(DeadzoneCircle, CanvasSize / 2 - deadzonePixelRadius);
        Canvas.SetTop(DeadzoneCircle, CanvasSize / 2 - deadzonePixelRadius);

        // Update labels
        var culture = CultureInfo.InvariantCulture;
        RawText.Text = $"{rawX.ToString("F2", culture)}, {rawY.ToString("F2", culture)}";
        ProcessedText.Text = $"{processedX.ToString("F2", culture)}, {processedY.ToString("F2", culture)}";
    }
}
