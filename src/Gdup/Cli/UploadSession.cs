using System.Diagnostics;
using System.Net;
using Gdup.Auth;
using Gdup.Config;
using Gdup.Drive;
using Gdup.Net;
using Gdup.Output;

namespace Gdup.Cli;

/// <summary>Wires a remote, the chosen NIC, the HttpClient and tokens together for one upload run.</summary>
internal sealed class UploadSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly TokenProvider _tokens;
    private readonly string _via;

    private UploadSession(Remote remote, HttpClient http, TokenProvider tokens, string via)
    {
        Remote = remote;
        _http = http;
        _tokens = tokens;
        _via = via;
        Drive = new DriveClient(http, tokens);
    }

    public Remote Remote { get; }
    public DriveClient Drive { get; }

    public static UploadSession Open(string remoteName, string? via)
    {
        var store = new ConfigStore(ConfigStore.DefaultPath);
        var remote = store.Load(remoteName)
            ?? throw new UsageException($"Remote '{remoteName}' not found in {store.FilePath}. Run: gdup config");
        if (remote.Token is null)
            throw new UsageException($"Remote '{remoteName}' is not signed in. Run: gdup config reconnect {remoteName}");

        IPAddress? bind = null;
        var viaText = "";
        if (via is not null)
        {
            var nics = NicSelector.ListUp();
            try
            {
                var (nic, address) = NicSelector.Select(via, nics);
                bind = address;
                viaText = $"{nic.Name} ({address})";
            }
            catch (NicNotFoundException ex)
            {
                throw new UsageException($"{ex.Message}{Environment.NewLine}Active interfaces:{Environment.NewLine}{NicsCommand.Render(nics)}");
            }
        }

        var http = HttpClientFactory.Create(bind);
        return new UploadSession(remote, http, new TokenProvider(remote, http, store.Save), viaText);
    }

    public async Task<int> RunAsync(UploadRequest request, CancellationToken ct)
    {
        var progress = new ProgressReporter(request.Name, _via, Console.Out, interactive: !Console.IsOutputRedirected);
        var uploader = new ResumableUploader(_http, _tokens, new SessionStore(SessionStore.DefaultDirectory), progress);
        var started = Stopwatch.StartNew();

        var result = await uploader.UploadAsync(request, ct);

        progress.Complete();
        Console.WriteLine($"Uploaded {result.Name} ({Format.Bytes(result.Size)}) in {Format.Duration(started.Elapsed)}, avg {Format.Rate(progress.AverageBytesPerSecond)}");
        Console.WriteLine($"Drive file id: {result.FileId}");
        switch (result.Md5Matches)
        {
            case true:
                Console.WriteLine($"MD5 verified: {result.LocalMd5}");
                return ExitCodes.Ok;
            case null:
                Console.WriteLine("Checksum not verified (MD5 unavailable).");
                return ExitCodes.Ok;
            default:
                Console.Error.WriteLine(
                    $"MD5 MISMATCH: local {result.LocalMd5}, Drive {result.DriveMd5}. The Drive file ({result.FileId}) was kept; check it or upload again.");
                return ExitCodes.ChecksumMismatch;
        }
    }

    public void Dispose() => _http.Dispose();
}
