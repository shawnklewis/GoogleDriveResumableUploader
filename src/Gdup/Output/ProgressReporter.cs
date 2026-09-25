using System.Diagnostics;
using Gdup.Drive;

namespace Gdup.Output;

/// <summary>
/// Interactive: one line redrawn in place about 4 times a second.
/// Redirected: a plain line every 10 seconds. Events always get their own line.
/// </summary>
public sealed class ProgressReporter(string fileName, string via, TextWriter output, bool interactive) : IUploadProgress
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RateWindow _window = new(TimeSpan.FromSeconds(5));
    private readonly TimeSpan _interval = interactive ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(10);
    private long _total;
    private long _position;
    private long _startPosition;
    private bool _started;
    private TimeSpan _nextDraw = TimeSpan.Zero;
    private int _lastLength;

    public double AverageBytesPerSecond
    {
        get
        {
            lock (_gate)
            {
                var seconds = _clock.Elapsed.TotalSeconds;
                return seconds > 0 ? (_position - _startPosition) / seconds : 0;
            }
        }
    }

    public void Start(long totalBytes)
    {
        lock (_gate) _total = totalBytes;
    }

    public void Position(long bytes)
    {
        lock (_gate)
        {
            if (!_started)
            {
                _startPosition = bytes;
                _started = true;
            }
            if (bytes < _position) _window.Reset();
            _position = bytes;
            var now = _clock.Elapsed;
            _window.Add(now.TotalSeconds, bytes);
            if (now >= _nextDraw)
            {
                _nextDraw = now + _interval;
                Draw(now);
            }
        }
    }

    public void Event(string message)
    {
        lock (_gate)
        {
            ClearLine();
            output.WriteLine(message);
            _nextDraw = TimeSpan.Zero;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            Draw(_clock.Elapsed);
            if (interactive)
            {
                output.WriteLine();
                _lastLength = 0;
            }
        }
    }

    internal string Line(TimeSpan now)
    {
        var percent = _total == 0 ? 100 : _position * 100.0 / _total;
        var rate = _window.BytesPerSecond;
        var average = now.TotalSeconds > 0 ? (_position - _startPosition) / now.TotalSeconds : 0;
        var eta = rate > 0 ? Format.Duration(TimeSpan.FromSeconds((_total - _position) / rate)) : "--:--:--";
        var suffix = via.Length > 0 ? $"  via {via}" : "";
        return $"{fileName}  {percent:0.0}%  {Format.Bytes(_position)}/{Format.Bytes(_total)}  "
             + $"{Format.Rate(rate)} (avg {Format.Rate(average)})  ETA {eta}{suffix}";
    }

    private void Draw(TimeSpan now)
    {
        var line = Line(now);
        if (interactive)
        {
            output.Write("\r" + line.PadRight(_lastLength));
            _lastLength = line.Length;
        }
        else
        {
            output.WriteLine(line);
        }
    }

    private void ClearLine()
    {
        if (!interactive || _lastLength == 0) return;
        output.Write("\r" + new string(' ', _lastLength) + "\r");
        _lastLength = 0;
    }
}
