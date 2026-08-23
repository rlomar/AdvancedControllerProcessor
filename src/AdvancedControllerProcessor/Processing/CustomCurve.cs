using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Custom response curve defined by user control points.
/// Uses piecewise linear interpolation between sorted points.
///
/// Constraints:
///   - First point must be (0, 0)
///   - Last point must be (1, 1)
///   - Points are sorted by X ascending
///   - Output is clamped to [0, 1]
///
/// If no points are provided, falls back to linear (identity).
/// </summary>
public sealed class CustomCurve : IResponseCurve
{
    private readonly List<CurvePoint> _points;

    public CustomCurve(List<CurvePoint> points)
    {
        _points = points
            .OrderBy(p => p.X)
            .ToList();

        // Ensure endpoints exist
        if (_points.Count == 0 || MathF.Abs(_points[0].X) > 0.001f)
            _points.Insert(0, new CurvePoint(0f, 0f));

        if (MathF.Abs(_points[^1].X - 1f) > 0.001f)
            _points.Add(new CurvePoint(1f, 1f));
    }

    public float Evaluate(float input)
    {
        if (_points.Count < 2)
            return input;

        float clamped = Math.Clamp(input, 0f, 1f);

        // Find the two surrounding points
        for (int i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i];
            var b = _points[i + 1];

            if (clamped >= a.X && clamped <= b.X)
            {
                // Linear interpolation between a and b
                float range = b.X - a.X;
                if (range < 0.0001f)
                    return Math.Clamp(a.Y, 0f, 1f);

                float t = (clamped - a.X) / range;
                float result = a.Y + t * (b.Y - a.Y);
                return Math.Clamp(result, 0f, 1f);
            }
        }

        // Fallback: return last point's Y
        return Math.Clamp(_points[^1].Y, 0f, 1f);
    }
}
