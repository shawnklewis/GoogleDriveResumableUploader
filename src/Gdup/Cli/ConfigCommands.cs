using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using Gdup.Auth;
using Gdup.Config;
using Gdup.Net;

namespace Gdup.Cli;

internal static class ConfigCommands
{
    private const string ClientHelp = """

        gdup signs in with your own Google OAuth client (type: Desktop app):
          1. Enable the Drive API: https://console.cloud.google.com/apis/library/drive.googleapis.com
          2. Create the client:    https://console.cloud.google.com/apis/credentials
             -> Create credentials -> OAuth client ID -> Application type: Desktop app
          3. Paste the client ID and client secret below.

        """;

    public static Command Create()
    {
        var config = new Command("config", "Create, edit, delete or list remotes (interactive)");
        config.SetAction((ParseResult _, CancellationToken ct) => CliRunner.RunAsync(() => WizardAsync(ct), ct));

        var showName = new Argument<string?>("remote") { Arity = ArgumentArity.ZeroOrOne, Description = "Remote to show (default: all)" };
        var show = new Command("show", "Show remotes with secrets masked");
        show.Arguments.Add(showName);
        show.SetAction((ParseResult pr, CancellationToken ct) =>
            CliRunner.RunAsync(() => Task.FromResult(Show(pr.GetValue(showName))), ct));

        var reconnectName = new Argument<string>("remote") { Description = "Remote to sign in again" };
        var reconnect = new Command("reconnect", "Redo the browser sign-in for an existing remote");
        reconnect.Arguments.Add(reconnectName);
        reconnect.SetAction((ParseResult pr, CancellationToken ct) =>
            CliRunner.RunAsync(() => ReconnectAsync(pr.GetValue(reconnectName)!, ct), ct));

        var importName = new Argument<string?>("name") { Arity = ArgumentArity.ZeroOrOne, Description = "rclone remote to import (default: choose from a list)" };
        var importPath = new Option<string?>("--rclone-config") { Description = @"Path to rclone.conf (default: %APPDATA%\rclone\rclone.conf)" };
        var import = new Command("import-rclone", "Copy a Google Drive remote from rclone.conf");
        import.Arguments.Add(importName);
        import.Options.Add(importPath);
        import.SetAction((ParseResult pr, CancellationToken ct) =>
            CliRunner.RunAsync(() => ImportAsync(pr.GetValue(importName), pr.GetValue(importPath), ct), ct));

        config.Subcommands.Add(show);
        config.Subcommands.Add(reconnect);
        config.Subcommands.Add(import);
        return config;
    }

    private static ConfigStore Store() => new(ConfigStore.DefaultPath);

    private static async Task<int> WizardAsync(CancellationToken ct)
    {
        var store = Store();
        while (true)
        {
            var names = store.RemoteNames();
            Console.WriteLine();
            Console.WriteLine(names.Count == 0 ? "No remotes configured." : "Remotes: " + string.Join(", ", names));
            Console.WriteLine("n) New remote   e) Edit remote   d) Delete remote   q) Quit");
            switch (Prompt.Ask("choice").ToLowerInvariant())
            {
                case "n":
                    await NewRemoteAsync(store, ct);
                    break;
                case "e" when names.Count > 0:
                    await EditRemoteAsync(store, Prompt.Choose("remote", names, n => n), ct);
                    break;
                case "d" when names.Count > 0:
                    var victim = Prompt.Choose("remote", names, n => n);
                    if (Prompt.Confirm($"Delete remote '{victim}'?")) store.Delete(victim);
                    break;
                case "q":
                    return ExitCodes.Ok;
            }
        }
    }

    private static async Task NewRemoteAsync(ConfigStore store, CancellationToken ct)
    {
        string name;
        while (true)
        {
            name = Prompt.AskRequired("Name for the new remote");
            if (name.IndexOfAny([':', '[', ']']) >= 0) Console.WriteLine("  Names cannot contain ':', '[' or ']'.");
            else if (store.Load(name) is not null) Console.WriteLine($"  Remote '{name}' already exists.");
            else break;
        }
        Console.WriteLine(ClientHelp);
        var remote = new Remote(
            name,
            Prompt.AskRequired("Client ID"),
            Prompt.AskRequired("Client secret"),
            AskScope("drive"),
            Prompt.Ask("Root folder ID (blank = My Drive)"),
            Token: null);
        store.Save(await SignInAsync(remote, ct));
        Console.WriteLine($"Remote '{name}' saved to {store.FilePath}");
    }

    private static async Task EditRemoteAsync(ConfigStore store, string name, CancellationToken ct)
    {
        var current = store.Load(name)!;
        var root = Prompt.Ask("Root folder ID (blank keeps current, '-' clears)", current.RootFolderId);
        var edited = current with
        {
            ClientId = Prompt.AskRequired("Client ID", current.ClientId),
            ClientSecret = Prompt.AskSecret("Client secret", current.ClientSecret),
            Scope = AskScope(current.Scope),
            RootFolderId = root == "-" ? "" : root,
        };
        var credentialsChanged = edited.ClientId != current.ClientId
            || edited.ClientSecret != current.ClientSecret
            || edited.Scope != current.Scope;
        if (credentialsChanged || edited.Token is null || Prompt.Confirm("Sign in again?"))
            edited = await SignInAsync(edited with { Token = null }, ct);
        store.Save(edited);
        Console.WriteLine($"Remote '{name}' saved.");
    }

    private static string AskScope(string current)
    {
        while (true)
        {
            var scope = Prompt.Ask("Scope (drive or drive.file)", current);
            if (scope is "drive" or "drive.file") return scope;
            Console.WriteLine("  Enter drive or drive.file.");
        }
    }

    private static int Show(string? name)
    {
        var store = Store();
        IReadOnlyList<string> names = name is null ? store.RemoteNames() : new[] { name };
        if (names.Count == 0)
        {
            Console.WriteLine($"No remotes in {store.FilePath}. Run: gdup config");
            return ExitCodes.Ok;
        }
        foreach (var n in names)
        {
            var r = store.Load(n) ?? throw new UsageException($"Remote '{n}' not found in {store.FilePath}.");
            var token = r.Token is null
                ? "not signed in"
                : $"signed in (access token expires {r.Token.Expiry.ToLocalTime():yyyy-MM-dd HH:mm})";
            Console.WriteLine($"[{r.Name}]");
            Console.WriteLine($"client_id      = {r.ClientId}");
            Console.WriteLine($"client_secret  = {(r.ClientSecret.Length > 0 ? "****" : "")}");
            Console.WriteLine($"scope          = {r.Scope}");
            Console.WriteLine($"root_folder_id = {r.RootFolderId}");
            Console.WriteLine($"token          = {token}");
            Console.WriteLine();
        }
        return ExitCodes.Ok;
    }

    private static async Task<int> ReconnectAsync(string name, CancellationToken ct)
    {
        var store = Store();
        var remote = store.Load(name) ?? throw new UsageException($"Remote '{name}' not found in {store.FilePath}.");
        store.Save(await SignInAsync(remote, ct));
        Console.WriteLine($"Remote '{name}' signed in.");
        return ExitCodes.Ok;
    }

    private static async Task<int> ImportAsync(string? name, string? rclonePath, CancellationToken ct)
    {
        var path = rclonePath ?? RcloneImporter.DefaultPath;
        if (!File.Exists(path)) throw new UsageException($"rclone config not found: {path}");
        var store = Store();

        IReadOnlyList<Remote> candidates;
        try
        {
            candidates = RcloneImporter.ReadDriveRemotes(File.ReadAllText(path));
        }
        catch (RcloneConfigEncryptedException ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine("Create a gdup remote with a fresh sign-in instead.");
            await NewRemoteAsync(store, ct);
            return ExitCodes.Ok;
        }
        if (candidates.Count == 0) throw new UsageException($"No Google Drive (type = drive) remotes in {path}.");

        var source = name is null
            ? Prompt.Choose("rclone remote to import", candidates, r => r.Name)
            : candidates.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
              ?? throw new UsageException($"rclone remote '{name}' not found or not a Google Drive remote.");

        var target = Prompt.AskRequired("Name for the gdup remote", source.Name);
        if (store.Load(target) is not null && !Prompt.Confirm($"Remote '{target}' already exists. Overwrite?"))
            return ExitCodes.Ok;

        var remote = source with { Name = target };
        if (remote.ClientId.Length == 0)
        {
            Console.WriteLine("This rclone remote uses rclone's built-in OAuth client, which gdup cannot reuse.");
            Console.WriteLine(ClientHelp);
            remote = remote with
            {
                ClientId = Prompt.AskRequired("Client ID"),
                ClientSecret = Prompt.AskRequired("Client secret"),
                Token = null,
            };
        }
        if (remote.Token is null) remote = await SignInAsync(remote, ct);

        store.Save(remote);
        Console.WriteLine($"Imported rclone remote '{source.Name}' as '{remote.Name}' into {store.FilePath}");
        return ExitCodes.Ok;
    }

    private static async Task<Remote> SignInAsync(Remote remote, CancellationToken ct)
    {
        Console.WriteLine("Opening your browser to sign in to Google. If it does not open, visit:");
        using var http = HttpClientFactory.Create(null);
        var token = await OAuthLoopback.AuthorizeAsync(http, remote.ClientId, remote.ClientSecret, remote.Scope, url =>
        {
            Console.WriteLine(url);
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                // No browser registered; the user can open the printed URL.
            }
        }, ct);
        Console.WriteLine("Signed in.");
        return remote with { Token = token };
    }
}
