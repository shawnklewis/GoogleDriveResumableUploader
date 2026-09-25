using System.CommandLine;
using System.Text;
using Gdup.Net;

namespace Gdup.Cli;

internal static class NicsCommand
{
    public static Command Create()
    {
        var cmd = new Command("nics", "List active network interfaces that can be used with --via");
        cmd.SetAction(_ =>
        {
            Console.Write(Render(NicSelector.ListUp()));
            return ExitCodes.Ok;
        });
        return cmd;
    }

    public static string Render(IReadOnlyList<NicInfo> nics)
    {
        if (nics.Count == 0) return "No active network interfaces found." + Environment.NewLine;
        var sb = new StringBuilder();
        foreach (var n in nics)
        {
            var speed = n.SpeedBitsPerSecond > 0 ? $"{n.SpeedBitsPerSecond / 1_000_000:N0} Mbps" : "speed unknown";
            sb.AppendLine($"{n.Name}  [{n.Description}]  {speed}");
            sb.AppendLine("    " + string.Join(", ", n.Addresses));
        }
        return sb.ToString();
    }
}
