using System;

namespace ScrollBoost.Acceleration;

public class PowerCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;
    private readonly double _gamma;
    private readonly double _maxMultiplier;
    private readonly double _scale;

    public PowerCurve(double baseMultiplier, double gamma, double maxMultiplier)
    {
        _baseMultiplier = baseMultiplier;
        _gamma = gamma;
        _maxMultiplier = maxMultiplier;
        _scale = (maxMultiplier - baseMultiplier) / Math.Pow(30.0, gamma);
    }

    public double Evaluate(double velocity)
    {
        double factor = _baseMultiplier + _scale * Math.Pow(velocity, _gamma);
        return Math.Min(factor, _maxMultiplier);
    }
}
