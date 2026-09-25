namespace Gdup.Drive;

public static class ContentTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".mov"] = "video/quicktime",
        [".avi"] = "video/x-msvideo",
        [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".gz"] = "application/gzip",
        [".tar"] = "application/x-tar",
        [".iso"] = "application/x-iso9660-image",
        [".txt"] = "text/plain",
        [".json"] = "application/json",
    };

    public static string For(string fileName) =>
        Map.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}
