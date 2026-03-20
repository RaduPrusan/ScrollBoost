using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class AccelerationEngineTests
{
    [Fact]
    public void SingleEvent_ReturnsOriginalDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);
        int result = engine.ProcessScroll(120, timestampMs: 1000);
        Assert.InRange(result, 100, 140);
    }

    [Fact]
    public void FastScrolling_AmplifiesDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);

        engine.ProcessScroll(120, 1000);
        engine.ProcessScroll(120, 1030);
        engine.ProcessScroll(120, 1060);
        int result = engine.ProcessScroll(120, 1090);

        Assert.True(result > 120, $"Fast scroll should amplify delta, got {result}");
    }

    [Fact]
    public void NegativeDelta_PreservesDirection()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(1.5, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);

        int result = engine.ProcessScroll(-120, 1000);
        Assert.True(result < 0, "Negative delta should remain negative");
    }

    [Fact]
    public void Disabled_ReturnsOriginalDelta()
    {
        var engine = new AccelerationEngine(
            new SigmoidCurve(2.0, 15.0, 15.0, 0.3),
            gestureTimeoutMs: 250);
        engine.Enabled = false;

        int result = engine.ProcessScroll(120, 1000);
        Assert.Equal(120, result);
    }
}
