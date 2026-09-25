using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gdup.Drive;

public sealed record SessionState(
    string SessionUri,
    string FilePath,
    long Size,
    long MtimeTicks,
    string Remote,
    string FolderId,
    string Name,
    long ChunkSize,
    DateTimeOffset Created);

/// <summary>Persists resumable session URIs so an upload survives the process being killed.</summary>
public sealed class SessionStore(string directory)
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gdup", "sessions");

    public static string KeyFor(string fullPath, long size, long mtimeTicks, string remote, string folderId, string name)
    {
        var raw = string.Join("|", fullPath.ToUpperInvariant(), size, mtimeTicks, remote, folderId, name);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public SessionState? Load(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path)) : null;
    }

    public void Save(string key, SessionState state)
    {
        Directory.CreateDirectory(directory);
        var tmp = PathFor(key) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, PathFor(key), overwrite: true);
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
    }

    public IReadOnlyList<(string Key, SessionState State)> List()
    {
        if (!Directory.Exists(directory)) return [];
        var entries = new List<(string Key, SessionState State)>();
        foreach (var path in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path)) is { } state)
                    entries.Add((Path.GetFileNameWithoutExtension(path), state));
            }
            catch (JsonException)
            {
                // Corrupt state file: ignore it; the upload will simply start a new session.
            }
        }
        return entries.OrderBy(e => e.State.Created).ToList();
    }

    /// <summary>Drops state for the same destination left over from an older version of the file.</summary>
    public void DeleteOtherVersions(string keepKey, string fullPath, string remote, string folderId, string name)
    {
        foreach (var (key, state) in List())
        {
            if (key != keepKey
                && string.Equals(state.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)
                && state.Remote == remote && state.FolderId == folderId && state.Name == name)
                Delete(key);
        }
    }

    private string PathFor(string key) => Path.Combine(directory, key + ".json");
}
