namespace Gdup.Cli;

/// <summary>A "remote:folder/path" destination.</summary>
public sealed record RemotePath(string Remote, string Path)
{
    public static RemotePath Parse(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0)
            throw new UsageException($"Destination '{text}' must look like <remote>:<folder/path>, e.g. gdrive:Backups/2026");
        return new RemotePath(text[..colon], text[(colon + 1)..].Trim('/', '\\'));
    }
}
