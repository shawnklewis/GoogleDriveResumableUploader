using Gdup.Auth;

namespace Gdup.Config;

public sealed class RcloneConfigEncryptedException()
    : Exception("rclone.conf is encrypted (RCLONE_ENCRYPT_V0), so gdup cannot read it.");

/// <summary>Reads Google Drive remotes from an rclone.conf file.</summary>
public static class RcloneImporter
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rclone", "rclone.conf");

    public static IReadOnlyList<Remote> ReadDriveRemotes(string text)
    {
        if (text.Split('\n').Any(line => line.Trim() == "RCLONE_ENCRYPT_V0:"))
            throw new RcloneConfigEncryptedException();

        var remotes = new List<Remote>();
        foreach (var s in IniFile.Parse(text).Sections)
        {
            if (!string.Equals(s["type"], "drive", StringComparison.OrdinalIgnoreCase)) continue;
            var token = s["token"];
            remotes.Add(new Remote(
                s.Name,
                s["client_id"] ?? "",
                s["client_secret"] ?? "",
                string.IsNullOrEmpty(s["scope"]) ? "drive" : s["scope"]!,
                s["root_folder_id"] ?? "",
                string.IsNullOrEmpty(token) ? null : OAuthToken.FromJson(token)));
        }
        return remotes;
    }
}
