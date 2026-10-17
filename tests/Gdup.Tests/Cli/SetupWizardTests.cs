using Gdup.Auth;
using Gdup.Cli;
using Gdup.Config;
using Gdup.Tests.Config;

namespace Gdup.Tests.Cli;

public class SetupWizardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly OAuthToken Token = new("ya29.access", "Bearer", "1//refresh", Now.AddHours(1));

    [Theory]
    [InlineData("gdup-123456")]
    [InlineData("abcdef")]
    [InlineData("a23456789012345678901234567890")]
    public void IsValidProjectId_Accepts(string id) => Assert.True(SetupWizard.IsValidProjectId(id));

    [Theory]
    [InlineData("abcde")]                           // too short
    [InlineData("a234567890123456789012345678901")] // 31 chars
    [InlineData("1gdup-project")]                   // starts with a digit
    [InlineData("Gdup-project")]                    // uppercase
    [InlineData("gdup_project")]                    // underscore
    [InlineData("gdup-project-")]                   // trailing hyphen
    [InlineData("")]
    public void IsValidProjectId_Rejects(string id) => Assert.False(SetupWizard.IsValidProjectId(id));

    [Fact]
    public async Task FromScratch_OpensEachPageInTheProject_AndSavesSignedInRemote()
    {
        using var dir = new TempDir();
        ClientSecretFileTests.Touch(dir.File("client_secret_123.json"), Now.AddMinutes(-1));
        // start, bad ID, good ID, 4x Enter, use found file, default name, default scope
        var (wizard, store, opened, output) = Create(dir, "1", "bad_id", "gdup-123456", "", "", "", "", "", "", "");

        Assert.Equal(ExitCodes.Ok, await wizard.RunAsync(CancellationToken.None));

        Assert.Equal(
        [
            "https://console.cloud.google.com/projectcreate",
            "https://console.cloud.google.com/apis/library/drive.googleapis.com?project=gdup-123456",
            "https://console.cloud.google.com/auth/branding?project=gdup-123456",
            "https://console.cloud.google.com/auth/audience?project=gdup-123456",
            "https://console.cloud.google.com/auth/clients/create?project=gdup-123456",
        ], opened);
        Assert.Equal(new Remote("gdrive", "123-abc.apps.googleusercontent.com", "GOCSPX-secret", "drive.file", "", Token), store.Load("gdrive"));
        var text = output.ToString();
        Assert.Contains("A project ID is 6-30", text);
        Assert.Contains("every 7 days", text);
        Assert.Contains("Google hasn't verified this app", text);
        Assert.Contains("gdup upload <file> gdrive:Backups", text);
        foreach (var url in opened) Assert.Contains(url, text);
    }

    [Fact]
    public async Task ExistingJson_SkipsConsoleSteps_AndAcceptsPastedPath()
    {
        using var dir = new TempDir();
        var json = dir.File("my-client.json");
        File.WriteAllText(json, ClientSecretFileTests.DesktopJson);
        var (wizard, store, opened, output) = Create(dir, "3", dir.File("nope.json"), json, "work", "drive");

        Assert.Equal(ExitCodes.Ok, await wizard.RunAsync(CancellationToken.None));

        Assert.Empty(opened);
        Assert.Contains("File not found", output.ToString());
        Assert.Equal("drive", store.Load("work")!.Scope);
    }

    [Fact]
    public async Task DecliningFoundFile_FallsBackToManualIdAndSecret_AndValidatesName()
    {
        using var dir = new TempDir();
        ClientSecretFileTests.Touch(dir.File("client_secret_123.json"), Now.AddMinutes(-1));
        var (wizard, store, _, output) = Create(dir, "3", "n", "", "my-id", "my-secret", "taken", "bad:name", "other", "");
        store.Save(new Remote("taken", "x", "y", "drive", "", Token));

        Assert.Equal(ExitCodes.Ok, await wizard.RunAsync(CancellationToken.None));

        Assert.Contains("already exists", output.ToString());
        Assert.Contains("cannot contain", output.ToString());
        var saved = store.Load("other")!;
        Assert.Equal(("my-id", "my-secret", "drive.file"), (saved.ClientId, saved.ClientSecret, saved.Scope));
    }

    [Fact]
    public async Task InputEndingMidway_SavesNothing()
    {
        using var dir = new TempDir();
        var (wizard, store, _, _) = Create(dir, "2", "gdup-123456", "");

        await Assert.ThrowsAsync<UsageException>(() => wizard.RunAsync(CancellationToken.None));

        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task CancelledDuringSignIn_SavesNothing()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        var (wizard, store, _, _) = Create(dir, ["3", "", "id", "secret", "", ""], (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => wizard.RunAsync(cts.Token));

        Assert.False(File.Exists(store.FilePath));
    }

    private static (SetupWizard, ConfigStore, List<string>, StringWriter) Create(TempDir dir, params string[] lines) =>
        Create(dir, lines, (r, _) => Task.FromResult(r with { Token = Token }));

    private static (SetupWizard, ConfigStore, List<string>, StringWriter) Create(
        TempDir dir, string[] lines, Func<Remote, CancellationToken, Task<Remote>> signIn)
    {
        var store = new ConfigStore(dir.File("gdup.conf"));
        var opened = new List<string>();
        var output = new StringWriter();
        var input = new StringReader(string.Join("\n", lines) + "\n");
        var wizard = new SetupWizard(input, output, store, opened.Add, signIn, dir.Path, new FixedTime(Now));
        return (wizard, store, opened, output);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
