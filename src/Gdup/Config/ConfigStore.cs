using Gdup.Auth;

namespace Gdup.Config;

/// <summary>Thrown when config secrets cannot be read due to decryption, encoding, or JSON parsing failures.</summary>
public sealed class ConfigCorruptException(string message, Exception inner) : Exception(message, inner);

/// <summary>Reads and writes remotes in gdup.conf; secrets are DPAPI-encrypted on save.</summary>
public sealed class ConfigStore(string path)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gdup", "gdup.conf");

    public string FilePath { get; } = path;

    public IReadOnlyList<string> RemoteNames() => Read().Sections.Select(s => s.Name).ToList();

    public Remote? Load(string name)
    {
        var s = Read().Get(name);
        if (s is null) return null;
        var token = s["token"];
        try
        {
            var unprotectedSecret = Dpapi.Unprotect(s["client_secret"] ?? "");
            OAuthToken? parsedToken = null;
            if (!string.IsNullOrEmpty(token))
            {
                parsedToken = OAuthToken.FromJson(Dpapi.Unprotect(token));
            }
            return new Remote(
                s.Name,
                s["client_id"] ?? "",
                unprotectedSecret,
                string.IsNullOrEmpty(s["scope"]) ? "drive" : s["scope"]!,
                s["root_folder_id"] ?? "",
                parsedToken);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException)
        {
            throw new ConfigCorruptException(
                $"Cannot read the saved secrets for remote '{name}' in {FilePath} (created by another Windows user or PC, or damaged). Delete and re-create it with: gdup config",
                ex);
        }
    }

    public void Save(Remote remote)
    {
        var ini = Read();
        var s = ini.GetOrAdd(remote.Name);
        s["type"] = "drive";
        s["client_id"] = remote.ClientId;
        s["client_secret"] = remote.ClientSecret.Length == 0 ? "" : Dpapi.Protect(remote.ClientSecret);
        s["scope"] = remote.Scope;
        s["root_folder_id"] = remote.RootFolderId;
        s["token"] = remote.Token is null ? "" : Dpapi.Protect(remote.Token.ToJson());
        Write(ini);
    }

    public bool Delete(string name)
    {
        var ini = Read();
        if (!ini.Remove(name)) return false;
        Write(ini);
        return true;
    }

    private IniFile Read() =>
        File.Exists(FilePath) ? IniFile.Parse(File.ReadAllText(FilePath)) : new IniFile();

    private void Write(IniFile ini)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ini.ToString());
        File.Move(tmp, FilePath, overwrite: true);
    }
}
