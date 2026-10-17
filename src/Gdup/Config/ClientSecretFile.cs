using System.Runtime.InteropServices;
using System.Text.Json;
using Gdup.Cli;

namespace Gdup.Config;

/// <summary>The client ID and secret from a client_secret_*.json downloaded from Google Cloud Console.</summary>
public sealed record ClientSecretFile(string ClientId, string ClientSecret)
{
    public const string FilePattern = "client_secret_*.json";

    /// <summary>Parses a Desktop app client file; throws <see cref="UsageException"/> for anything else.</summary>
    public static ClientSecretFile Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new UsageException("The client secret file is not valid JSON. Download it again from Google Cloud Console.");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new UsageException("The client secret file has no \"installed\" section. Download the JSON of a Desktop app client.");
            if (!root.TryGetProperty("installed", out var installed))
            {
                throw new UsageException(root.TryGetProperty("web", out _)
                    ? "This is a Web application client. Create a Desktop app client instead."
                    : "The client secret file has no \"installed\" section. Download the JSON of a Desktop app client.");
            }
            return new ClientSecretFile(Field(installed, "client_id"), Field(installed, "client_secret"));
        }
    }

    public static ClientSecretFile Load(string path)
    {
        path = path.Trim().Trim('"');
        if (!File.Exists(path)) throw new UsageException($"File not found: {path}");
        return Parse(File.ReadAllText(path));
    }

    /// <summary>The newest client_secret_*.json in <paramref name="directory"/> created or written within <paramref name="maxAge"/>.</summary>
    public static string? FindRecent(string? directory, DateTimeOffset now, TimeSpan maxAge)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        return new DirectoryInfo(directory)
            .EnumerateFiles(FilePattern)
            .Select(f => (f.FullName, Time: f.CreationTimeUtc > f.LastWriteTimeUtc ? f.CreationTimeUtc : f.LastWriteTimeUtc))
            .Where(f => now.UtcDateTime - f.Time <= maxAge)
            .OrderByDescending(f => f.Time)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    /// <summary>The user's Downloads folder (honours a relocated folder), or null if Windows cannot resolve it.</summary>
    public static string? DownloadsFolder()
    {
        var folderId = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        var hr = SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var pathPtr);
        try
        {
            return hr == 0 ? Marshal.PtrToStringUni(pathPtr) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    private static string Field(JsonElement section, string name)
    {
        if (section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text)
            return text;
        throw new UsageException($"The client secret file is missing \"{name}\". Download it again from Google Cloud Console.");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
