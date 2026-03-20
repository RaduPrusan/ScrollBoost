using System;

namespace ScrollBoost.Acceleration;

public class AccelerationEngine
{
    private readonly VelocityTracker _velocityTracker;
    private IAccelerationCurve _curve;

    public bool Enabled { get; set; } = true;

    public AccelerationEngine(IAccelerationCurve curve, double gestureTimeoutMs = 250,
        int windowSize = 4, double smoothingAlpha = 0.3)
    {
        _curve = curve;
        _velocityTracker = new VelocityTracker(windowSize, gestureTimeoutMs, smoothingAlpha);
    }

    public void SetCurve(IAccelerationCurve curve)
    {
        _curve = curve;
    }

    public int ProcessScroll(int originalDelta, long timestampMs)
    {
        if (!Enabled) return originalDelta;

        double velocity = _velocityTracker.RecordEventAndGetVelocity(timestampMs);
        double factor = _curve.Evaluate(velocity);

        double result = originalDelta * factor;

        if (result > 0)
            return Math.Max((int)Math.Ceiling(result), originalDelta);
        else if (result < 0)
            return Math.Min((int)Math.Floor(result), originalDelta);
        else
            return 0;
    }
}
