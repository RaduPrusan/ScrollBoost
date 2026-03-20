namespace ScrollBoost.Acceleration;

public class LinearCurve : IAccelerationCurve
{
    private readonly double _baseMultiplier;

    public LinearCurve(double baseMultiplier)
    {
        _baseMultiplier = baseMultiplier;
    }

    public double Evaluate(double velocity) => _baseMultiplier;
}
