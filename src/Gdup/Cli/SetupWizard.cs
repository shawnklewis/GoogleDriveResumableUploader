using System.CommandLine;
using System.Text.RegularExpressions;
using Gdup.Config;

namespace Gdup.Cli;

internal static class SetupCommand
{
    public static Command Create()
    {
        var setup = new Command("setup", "Guided first-time setup: create your own Google OAuth client and sign in");
        setup.SetAction((ParseResult _, CancellationToken ct) => CliRunner.RunAsync(() =>
        {
            var wizard = new SetupWizard(
                Console.In, Console.Out, new ConfigStore(ConfigStore.DefaultPath),
                ConfigCommands.OpenBrowser, ConfigCommands.SignInAsync,
                ClientSecretFile.DownloadsFolder(), TimeProvider.System);
            return wizard.RunAsync(ct);
        }, ct));
        return setup;
    }
}

/// <summary>
/// Walks the user through creating their own Google Cloud project and Desktop OAuth client,
/// then signs in and saves a remote. Nothing is saved until sign-in succeeds.
/// </summary>
internal sealed partial class SetupWizard(
    TextReader input,
    TextWriter output,
    ConfigStore store,
    Action<string> openBrowser,
    Func<Remote, CancellationToken, Task<Remote>> signIn,
    string? downloadsFolder,
    TimeProvider clock)
{
    public static readonly TimeSpan RecentDownload = TimeSpan.FromMinutes(30);
    private const string ConsoleUrl = "https://console.cloud.google.com";
    private const int Steps = 8;

    [GeneratedRegex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$")]
    private static partial Regex ProjectIdPattern();

    /// <summary>Google Cloud project IDs: 6-30 chars of lowercase letters, digits and hyphens, starting with a letter.</summary>
    public static bool IsValidProjectId(string id) => ProjectIdPattern().IsMatch(id);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        output.WriteLine("gdup setup: create your own Google OAuth client and sign in (about 5 minutes).");
        output.WriteLine("Each step opens a Google Cloud Console page; the URL is printed too.");
        output.WriteLine();
        output.WriteLine("  1) Start from scratch");
        output.WriteLine("  2) I already have a Google Cloud project");
        output.WriteLine("  3) I already have a client_secret.json");
        var start = AskChoice("choice", "1", ["1", "2", "3"], ct);

        if (start != "3")
        {
            var project = start == "1" ? CreateProject(ct) : AskProjectId(ct);
            ConsoleSteps(project, ct);
        }

        var client = ImportClient(ct);
        var (name, scope) = AskNameAndScope(ct);

        Header(8, "Sign in");
        output.WriteLine("  Google will say \"Google hasn't verified this app\". That is expected: it is your own app.");
        output.WriteLine("  Click Advanced, then Go to <your app name> (unsafe), then Continue.");
        var remote = await signIn(new Remote(name, client.ClientId, client.ClientSecret, scope, "", Token: null), ct);
        ct.ThrowIfCancellationRequested();
        store.Save(remote);

        output.WriteLine();
        output.WriteLine($"Done. Remote '{name}' saved to {store.FilePath}");
        output.WriteLine($"Try: gdup upload <file> {name}:Backups");
        return ExitCodes.Ok;
    }

    private string CreateProject(CancellationToken ct)
    {
        Header(1, "Create a project");
        output.WriteLine("  Pick any name, click Create, then copy the project ID shown under the name.");
        Open($"{ConsoleUrl}/projectcreate");
        return AskProjectId(ct);
    }

    private string AskProjectId(CancellationToken ct)
    {
        while (true)
        {
            var id = Ask("Project ID", null, ct).ToLowerInvariant();
            if (IsValidProjectId(id)) return id;
            output.WriteLine("  A project ID is 6-30 lowercase letters, digits or hyphens, starting with a letter (e.g. gdup-123456).");
        }
    }

    private void ConsoleSteps(string project, CancellationToken ct)
    {
        var q = "?project=" + Uri.EscapeDataString(project);

        Header(2, "Enable the Google Drive API");
        output.WriteLine("  Click Enable (if it says Manage, it is already enabled).");
        Open($"{ConsoleUrl}/apis/library/drive.googleapis.com{q}");
        WaitForEnter(ct);

        Header(3, "Configure the consent screen");
        output.WriteLine("  If you see Get started: app name gdup, your email as support email, Audience External,");
        output.WriteLine("  your email as contact, accept the policy, Create.");
        Open($"{ConsoleUrl}/auth/branding{q}");
        WaitForEnter(ct);

        Header(4, "Publish the app  <-- do not skip");
        output.WriteLine("  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        output.WriteLine("  !!  Click Publish app, then Confirm.                        !!");
        output.WriteLine("  !!  Left in Testing, Google signs gdup out every 7 days.    !!");
        output.WriteLine("  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        output.WriteLine("  A note about verification is fine to ignore for your own use.");
        Open($"{ConsoleUrl}/auth/audience{q}");
        WaitForEnter(ct);

        Header(5, "Create the OAuth client");
        output.WriteLine("  Application type: Desktop app, any name, Create, then Download JSON.");
        Open($"{ConsoleUrl}/auth/clients/create{q}");
        WaitForEnter(ct);
    }

    private ClientSecretFile ImportClient(CancellationToken ct)
    {
        Header(6, "Import the client");
        var found = ClientSecretFile.FindRecent(downloadsFolder, clock.GetUtcNow(), RecentDownload);
        if (found is not null && Confirm($"Use {Path.GetFileName(found)} from Downloads?", defaultYes: true, ct))
        {
            try
            {
                return ClientSecretFile.Load(found);
            }
            catch (UsageException ex)
            {
                output.WriteLine("  " + ex.Message);
            }
        }
        while (true)
        {
            var path = Ask("Path to client_secret JSON (blank to type the ID and secret)", null, ct);
            if (path.Length == 0)
                return new ClientSecretFile(AskRequired("Client ID", ct), AskRequired("Client secret", ct));
            try
            {
                return ClientSecretFile.Load(path);
            }
            catch (UsageException ex)
            {
                output.WriteLine("  " + ex.Message);
            }
        }
    }

    private (string Name, string Scope) AskNameAndScope(CancellationToken ct)
    {
        Header(7, "Name the remote");
        string name;
        while (true)
        {
            name = AskRequired("Remote name", ct, store.Load("gdrive") is null ? "gdrive" : null);
            var error = ConfigCommands.RemoteNameError(store, name);
            if (error is null) break;
            output.WriteLine("  " + error);
        }
        output.WriteLine("  drive.file: gdup only sees folders it created (safer). drive: full access to your Drive.");
        var scope = AskChoice("Scope", "drive.file", ["drive.file", "drive"], ct);
        return (name, scope);
    }

    private void Header(int step, string title)
    {
        output.WriteLine();
        output.WriteLine($"[{step}/{Steps}] {title}");
    }

    private void Open(string url)
    {
        output.WriteLine("  " + url);
        openBrowser(url);
    }

    private void WaitForEnter(CancellationToken ct)
    {
        output.Write("Press Enter when done ");
        ReadLine(ct);
    }

    private string ReadLine(CancellationToken ct)
    {
        var line = input.ReadLine();
        ct.ThrowIfCancellationRequested();
        return line?.Trim() ?? throw new UsageException("Input ended before setup finished. Nothing was saved.");
    }

    private string Ask(string label, string? defaultValue, CancellationToken ct)
    {
        output.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var line = ReadLine(ct);
        return line.Length == 0 ? defaultValue ?? "" : line;
    }

    private string AskRequired(string label, CancellationToken ct, string? defaultValue = null)
    {
        while (true)
        {
            var value = Ask(label, defaultValue, ct);
            if (value.Length > 0) return value;
            output.WriteLine("  A value is required.");
        }
    }

    private string AskChoice(string label, string defaultValue, string[] choices, CancellationToken ct)
    {
        while (true)
        {
            var value = Ask(label, defaultValue, ct).ToLowerInvariant();
            if (choices.Contains(value)) return value;
            output.WriteLine($"  Enter {string.Join(", ", choices[..^1])} or {choices[^1]}.");
        }
    }

    private bool Confirm(string question, bool defaultYes, CancellationToken ct)
    {
        var answer = Ask($"{question} ({(defaultYes ? "Y/n" : "y/N")})", null, ct);
        return answer.Length == 0 ? defaultYes : answer.StartsWith('y') || answer.StartsWith('Y');
    }
}
