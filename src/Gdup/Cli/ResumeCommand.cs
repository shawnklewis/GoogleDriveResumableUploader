using System.CommandLine;
using Gdup.Drive;
using Gdup.Output;

namespace Gdup.Cli;

internal static class ResumeCommand
{
    public static Command Create()
    {
        var file = new Argument<string?>("file") { Arity = ArgumentArity.ZeroOrOne, Description = "Local file of an interrupted upload (omit to list)" };
        var list = new Option<bool>("--list") { Description = "List interrupted uploads" };
        var via = new Option<string?>("--via") { Description = "Network interface name or local IP to upload through" };
        var stall = new Option<int>("--stall-timeout") { Description = "Seconds without progress before reconnecting (min 5)", DefaultValueFactory = _ => 60 };

        var cmd = new Command("resume", "List or continue interrupted uploads");
        cmd.Arguments.Add(file);
        cmd.Options.Add(list);
        cmd.Options.Add(via);
        cmd.Options.Add(stall);
        cmd.SetAction((ParseResult pr, CancellationToken ct) => CliRunner.RunAsync(async () =>
        {
            var sessions = new SessionStore(SessionStore.DefaultDirectory).List();
            var target = pr.GetValue(file);
            if (target is null || pr.GetValue(list))
            {
                if (sessions.Count == 0) Console.WriteLine("No interrupted uploads.");
                foreach (var (_, s) in sessions)
                {
                    Console.WriteLine(s.FilePath);
                    Console.WriteLine($"    -> {s.Remote}: {s.Name}  {Format.Bytes(s.Size)}  started {s.Created.ToLocalTime():yyyy-MM-dd HH:mm}");
                }
                return ExitCodes.Ok;
            }

            var fullPath = Path.GetFullPath(target);
            var state = sessions.Select(e => e.State)
                .LastOrDefault(s => string.Equals(s.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new UsageException($"No interrupted upload found for {fullPath}. Run: gdup resume --list");
            if (!File.Exists(state.FilePath)) throw new UsageException($"File not found: {state.FilePath}");

            using var session = UploadSession.Open(state.Remote, pr.GetValue(via));
            var request = new UploadRequest(state.FilePath, state.Remote, state.FolderId, state.Name, state.ChunkSize,
                UploadCommand.StallTimeout(pr.GetValue(stall)));
            return await session.RunAsync(request, ct);
        }, ct));
        return cmd;
    }
}
