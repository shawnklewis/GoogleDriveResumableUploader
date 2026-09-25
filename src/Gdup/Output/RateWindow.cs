namespace Gdup.Output;

/// <summary>Transfer rate over the most recent <c>window</c> of (time, position) samples.</summary>
public sealed class RateWindow(TimeSpan window)
{
    private readonly Queue<(double Seconds, long Position)> _samples = new();
    private (double Seconds, long Position) _last;

    public double BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2) return 0;
            var first = _samples.Peek();
            var elapsed = _last.Seconds - first.Seconds;
            return elapsed <= 0 ? 0 : Math.Max(0, (_last.Position - first.Position) / elapsed);
        }
    }

    public void Add(double seconds, long position)
    {
        _last = (seconds, position);
        _samples.Enqueue(_last);
        while (_samples.Count > 2 && seconds - _samples.Peek().Seconds > window.TotalSeconds)
            _samples.Dequeue();
    }

    public void Reset() => _samples.Clear();
}
