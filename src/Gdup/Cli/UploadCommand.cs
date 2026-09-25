using System.CommandLine;
using Gdup.Drive;

namespace Gdup.Cli;

internal static class UploadCommand
{
    public static Command Create()
    {
        var file = new Argument<string>("file") { Description = "Local file to upload" };
        var destination = new Argument<string>("destination") { Description = "<remote>:<folder/path> in Google Drive" };
        var via = new Option<string?>("--via") { Description = "Network interface name or local IP to upload through (see: gdup nics)" };
        var chunk = new Option<string>("--chunk-size") { Description = "Chunk size: multiple of 256K, max 4G", DefaultValueFactory = _ => "256M" };
        var name = new Option<string?>("--name") { Description = "File name in Drive (default: the local file name)" };
        var stall = new Option<int>("--stall-timeout") { Description = "Seconds without progress before reconnecting (min 5)", DefaultValueFactory = _ => 60 };

        var cmd = new Command("upload", "Upload a file to Google Drive using the resumable protocol");
        cmd.Arguments.Add(file);
        cmd.Arguments.Add(destination);
        cmd.Options.Add(via);
        cmd.Options.Add(chunk);
        cmd.Options.Add(name);
        cmd.Options.Add(stall);
        cmd.SetAction((ParseResult pr, CancellationToken ct) => CliRunner.RunAsync(async () =>
        {
            var target = RemotePath.Parse(pr.GetValue(destination)!);
            var path = Path.GetFullPath(pr.GetValue(file)!);
            if (!File.Exists(path)) throw new UsageException($"File not found: {path}");
            var chunkSize = SizeParser.ParseChunkSize(pr.GetValue(chunk)!);
            var stallTimeout = StallTimeout(pr.GetValue(stall));

            using var session = UploadSession.Open(target.Remote, pr.GetValue(via));
            var folderId = await session.Drive.ResolveFolderAsync(session.Remote.RootFolderId, target.Path, ct);
            var request = new UploadRequest(path, target.Remote, folderId, pr.GetValue(name) ?? Path.GetFileName(path), chunkSize, stallTimeout);
            return await session.RunAsync(request, ct);
        }, ct));
        return cmd;
    }

    internal static TimeSpan StallTimeout(int seconds) =>
        seconds >= 5 ? TimeSpan.FromSeconds(seconds) : throw new UsageException("--stall-timeout must be at least 5 seconds.");
}
