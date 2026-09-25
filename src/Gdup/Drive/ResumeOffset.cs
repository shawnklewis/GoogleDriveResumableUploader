namespace Gdup.Drive;

public static class ResumeOffset
{
    /// <summary>Drive's 308 carries "Range: bytes=0-N" for the committed prefix; no header means nothing is committed.</summary>
    public static long FromResponse(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Range", out var values) ? Parse(values.FirstOrDefault()) : 0;

    public static long Parse(string? rangeHeader)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader)) return 0;
        var dash = rangeHeader.LastIndexOf('-');
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)
            || dash < 0
            || !long.TryParse(rangeHeader.AsSpan(dash + 1), out var lastByte))
            throw new FormatException($"Unexpected Range header '{rangeHeader}'.");
        return lastByte + 1;
    }
}
