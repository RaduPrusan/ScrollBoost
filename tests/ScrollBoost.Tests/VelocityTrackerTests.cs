using ScrollBoost.Acceleration;

namespace ScrollBoost.Tests;

public class VelocityTrackerTests
{
    [Fact]
    public void FirstEvent_ReturnsZeroVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 0.3);
        double velocity = tracker.RecordEventAndGetVelocity(1000);
        Assert.Equal(0.0, velocity);
    }

    [Fact]
    public void TwoEvents_50msApart_Returns20NotchesPerSec()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        double velocity = tracker.RecordEventAndGetVelocity(1050);
        Assert.Equal(20.0, velocity, precision: 1);
    }

    [Fact]
    public void EventAfterTimeout_ResetsVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        tracker.RecordEventAndGetVelocity(1050);
        double velocity = tracker.RecordEventAndGetVelocity(1550);
        Assert.Equal(0.0, velocity);
    }

    [Fact]
    public void MultipleEvents_ComputesWindowedVelocity()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 1.0);
        tracker.RecordEventAndGetVelocity(1000);
        tracker.RecordEventAndGetVelocity(1040);
        tracker.RecordEventAndGetVelocity(1080);
        double velocity = tracker.RecordEventAndGetVelocity(1120);
        Assert.Equal(25.0, velocity, precision: 1);
    }

    [Fact]
    public void EmaSmoothing_DampensSpikes()
    {
        var tracker = new VelocityTracker(windowSize: 4, gestureTimeoutMs: 250, smoothingAlpha: 0.3);
        tracker.RecordEventAndGetVelocity(1000);
        double v1 = tracker.RecordEventAndGetVelocity(1100);
        double v2 = tracker.RecordEventAndGetVelocity(1120);
        Assert.True(v2 < 50.0, $"EMA should dampen spike, got {v2}");
        Assert.True(v2 > v1, $"Velocity should increase, got {v2} vs {v1}");
    }
}
