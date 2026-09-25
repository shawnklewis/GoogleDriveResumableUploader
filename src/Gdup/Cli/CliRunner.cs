using Gdup.Config;
using Gdup.Net;

namespace Gdup.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Failed = 2;
    public const int ChecksumMismatch = 3;
}

/// <summary>Runs a command body and turns exceptions into messages and exit codes.</summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(Func<Task<int>> body, CancellationToken ct, TextWriter? error = null)
    {
        error ??= Console.Error;
        try
        {
            return await body();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error.WriteLine();
            error.WriteLine("Cancelled. Run the same command again, or `gdup resume`, to continue where it stopped.");
            return ExitCodes.Failed;
        }
        catch (Exception ex) when (ex is UsageException or NicNotFoundException or RcloneConfigEncryptedException or ConfigCorruptException)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception ex)
        {
            error.WriteLine($"error: {Describe(ex)}");
            return ExitCodes.Failed;
        }
    }

    internal static string Describe(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!messages.Contains(e.Message)) messages.Add(e.Message);
        }
        return string.Join(" -> ", messages);
    }
}
