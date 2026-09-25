using System.Globalization;

namespace Gdup.Cli;

public static class SizeParser
{
    public const long Quantum = 256 * 1024;
    public const long MaxChunk = 4L * 1024 * 1024 * 1024;

    /// <summary>Parses sizes like 256M, 512K, 1G, 256MB or plain bytes (binary multiples).</summary>
    public static long ParseChunkSize(string text)
    {
        var t = text.Trim().ToUpperInvariant();
        if (t.Length > 1 && t.EndsWith('B') && char.IsLetter(t[^2])) t = t[..^1];

        long multiplier = 1;
        if (t.Length > 0 && char.IsLetter(t[^1]))
        {
            multiplier = t[^1] switch
            {
                'K' => 1024L,
                'M' => 1024L * 1024,
                'G' => 1024L * 1024 * 1024,
                _ => throw new UsageException($"Unknown size suffix in '{text}'. Use K, M or G."),
            };
            t = t[..^1];
        }

        if (!long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new UsageException($"Invalid size '{text}'.");
        if (number > MaxChunk / multiplier)
            throw new UsageException($"Chunk size must be at most 4G; got {text}.");

        var bytes = number * multiplier;
        if (bytes % Quantum != 0)
            throw new UsageException($"Chunk size must be a multiple of 256K; got {text}.");
        return bytes;
    }
}
