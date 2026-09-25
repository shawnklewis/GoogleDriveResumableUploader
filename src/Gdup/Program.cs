using System.CommandLine;
using Gdup.Cli;

var root = new RootCommand("gdup - fast resumable uploads of large files to Google Drive");
root.Subcommands.Add(ConfigCommands.Create());
root.Subcommands.Add(NicsCommand.Create());
root.Subcommands.Add(UploadCommand.Create());
root.Subcommands.Add(ResumeCommand.Create());

// Own Ctrl+C handling: cancel the token and let the uploader stop cleanly (state is kept for resume).
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, cts.Token);
