using System;

namespace ScrollBoost.Acceleration;

public class SigmoidCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;
    private readonly double _maxMultiplier;
    private readonly double _midpoint;
    private readonly double _steepness;

    public SigmoidCurve(double baseMultiplier, double maxMultiplier, double midpoint, double steepness)
    {
        _baseMultiplier = baseMultiplier;
        _maxMultiplier = maxMultiplier;
        _midpoint = midpoint;
        _steepness = steepness;
    }

    public double Evaluate(double velocity)
    {
        double sigmoid = 1.0 / (1.0 + Math.Exp(-_steepness * (velocity - _midpoint)));
        return _baseMultiplier + (_maxMultiplier - _baseMultiplier) * sigmoid;
    }
}
