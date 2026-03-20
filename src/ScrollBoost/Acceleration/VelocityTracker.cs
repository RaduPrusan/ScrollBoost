namespace ScrollBoost.Acceleration;

public class VelocityTracker
{
    private readonly int _windowSize;
    private readonly double _gestureTimeoutMs;
    private readonly double _smoothingAlpha;
    private readonly long[] _timestamps;
    private int _count;
    private int _head;
    private double _smoothedVelocity;

    public VelocityTracker(int windowSize = 4, double gestureTimeoutMs = 250, double smoothingAlpha = 0.3)
    {
        _windowSize = windowSize;
        _gestureTimeoutMs = gestureTimeoutMs;
        _smoothingAlpha = smoothingAlpha;
        _timestamps = new long[windowSize];
        _count = 0;
        _head = 0;
        _smoothedVelocity = 0;
    }

    public double RecordEventAndGetVelocity(long timestampMs)
    {
        if (_count > 0)
        {
            int lastIndex = (_head - 1 + _windowSize) % _windowSize;
            long lastTimestamp = _timestamps[lastIndex];
            if (timestampMs - lastTimestamp > _gestureTimeoutMs)
            {
                _count = 0;
                _head = 0;
                _smoothedVelocity = 0;
            }
        }

        _timestamps[_head] = timestampMs;
        _head = (_head + 1) % _windowSize;
        if (_count < _windowSize) _count++;

        if (_count < 2)
        {
            _smoothedVelocity = 0;
            return 0;
        }

        int oldest = _count < _windowSize
            ? 0
            : _head;
        int newest = (_head - 1 + _windowSize) % _windowSize;

        int oldestIndex = _count < _windowSize
            ? (_head - _count + _windowSize) % _windowSize
            : _head;

        long oldestTimestamp = _timestamps[oldestIndex];
        long newestTimestamp = _timestamps[newest];
        double timeSpanMs = newestTimestamp - oldestTimestamp;

        if (timeSpanMs <= 0) return _smoothedVelocity;

        double rawVelocity = (_count - 1) / (timeSpanMs / 1000.0);

        _smoothedVelocity = _smoothingAlpha * rawVelocity + (1 - _smoothingAlpha) * _smoothedVelocity;

        return _smoothedVelocity;
    }

    public void Reset()
    {
        _count = 0;
        _head = 0;
        _smoothedVelocity = 0;
    }
}
