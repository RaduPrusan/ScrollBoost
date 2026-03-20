using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class AccelerationCurveTests
{
    [Fact]
    public void Linear_ReturnsBaseMultiplier_Regardless()
    {
        var curve = new LinearCurve(baseMultiplier: 2.0);
        Assert.Equal(2.0, curve.Evaluate(0));
        Assert.Equal(2.0, curve.Evaluate(10));
        Assert.Equal(2.0, curve.Evaluate(100));
    }

    [Fact]
    public void Power_AtZeroVelocity_ReturnsBase()
    {
        var curve = new PowerCurve(baseMultiplier: 1.5, gamma: 2.0, maxMultiplier: 20.0);
        double factor = curve.Evaluate(0);
        Assert.Equal(1.5, factor, precision: 2);
    }

    [Fact]
    public void Power_IncreasesWithVelocity()
    {
        var curve = new PowerCurve(baseMultiplier: 1.0, gamma: 2.0, maxMultiplier: 50.0);
        double slow = curve.Evaluate(5);
        double fast = curve.Evaluate(20);
        Assert.True(fast > slow, $"fast={fast} should be > slow={slow}");
    }

    [Fact]
    public void Power_ClampsAtMax()
    {
        var curve = new PowerCurve(baseMultiplier: 1.0, gamma: 2.0, maxMultiplier: 10.0);
        double extreme = curve.Evaluate(1000);
        Assert.Equal(10.0, extreme, precision: 2);
    }

    [Fact]
    public void Sigmoid_AtZeroVelocity_ReturnsNearBase()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(0);
        Assert.True(factor < 2.0, $"At v=0, factor should be near base, got {factor}");
    }

    [Fact]
    public void Sigmoid_AtMidpoint_ReturnsHalfway()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(15);
        double expected = (1.0 + 15.0) / 2.0; // 8.0
        Assert.Equal(expected, factor, precision: 0);
    }

    [Fact]
    public void Sigmoid_AtHighVelocity_ApproachesMax()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double factor = curve.Evaluate(100);
        Assert.True(factor > 14.0, $"At v=100, factor should approach max, got {factor}");
    }

    [Fact]
    public void Sigmoid_IsMonotonicallyIncreasing()
    {
        var curve = new SigmoidCurve(baseMultiplier: 1.0, maxMultiplier: 15.0, midpoint: 15.0, steepness: 0.3);
        double prev = curve.Evaluate(0);
        for (int v = 1; v <= 50; v++)
        {
            double current = curve.Evaluate(v);
            Assert.True(current >= prev, $"Curve should be monotonic: v={v}, current={current}, prev={prev}");
            prev = current;
        }
    }
}
