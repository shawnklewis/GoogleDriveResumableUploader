using System.CommandLine;
using Gdup.Cli;

var root = new RootCommand("gdup - fast resumable uploads of large files to Google Drive");
root.Subcommands.Add(SetupCommand.Create());
root.Subcommands.Add(ConfigCommands.Create());
root.Subcommands.Add(NicsCommand.Create());
root.Subcommands.Add(UploadCommand.Create());
root.Subcommands.Add(ResumeCommand.Create());

// Own Ctrl+C handling: cancel the token and let the uploader stop cleanly (state is kept for resume).
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C: cancel gracefully and let the process keep running to save state.
    // Second Ctrl+C (cts already cancelled): let the default handler terminate the process.
    e.Cancel = !cts.IsCancellationRequested;
    cts.Cancel();
};

return await root.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, cts.Token);
