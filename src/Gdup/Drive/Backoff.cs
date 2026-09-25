namespace Gdup.Drive;

/// <summary>Shared exponential backoff with equal jitter (half fixed + half random), 1s -> 64s cap.</summary>
internal static class Backoff
{
    private const double MaxSeconds = 64;

    public static TimeSpan For(int attempt)
    {
        var cap = Math.Min(MaxSeconds, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(cap / 2 + Random.Shared.NextDouble() * cap / 2);
    }
}
