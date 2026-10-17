using Gdup.Auth;
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
        // Once the token is cancelled, any exception (e.g. an IOException from an aborted socket,
        // not just OperationCanceledException) is a symptom of the cancellation, not a real failure.
        catch (Exception) when (ct.IsCancellationRequested)
        {
            error.WriteLine();
            error.WriteLine("Cancelled. If an upload was interrupted, run the same command again or `gdup resume` to continue.");
            return ExitCodes.Failed;
        }
        catch (Exception ex) when (ex is UsageException or NicNotFoundException or RcloneConfigEncryptedException or ConfigCorruptException)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (AuthExpiredException ex) when (ex.InnerException is OAuthException { Error: "invalid_grant" })
        {
            error.WriteLine($"error: {TestingModeExpiryHint(ex.RemoteName)}");
            return ExitCodes.Failed;
        }
        catch (Exception ex)
        {
            error.WriteLine($"error: {Describe(ex)}");
            return ExitCodes.Failed;
        }
    }

    internal static string TestingModeExpiryHint(string remoteName) =>
        "Google rejected the saved sign-in. If your consent screen is still in Testing, Google expires sign-ins after 7 days. "
        + $"Publish it at https://console.cloud.google.com/auth/audience, then run: gdup config reconnect {remoteName}";

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
