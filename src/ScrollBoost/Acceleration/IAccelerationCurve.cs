namespace ScrollBoost.Acceleration;

public interface IAccelerationCurve
{
    double Evaluate(double velocity);
}
