namespace Gdup.Cli;

internal static class Prompt
{
    public static string Ask(string label, string? defaultValue = null)
    {
        Console.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var line = Console.ReadLine() ?? throw new UsageException("Input ended before the question was answered.");
        line = line.Trim();
        return line.Length == 0 ? defaultValue ?? "" : line;
    }

    public static string AskRequired(string label, string? defaultValue = null)
    {
        while (true)
        {
            var value = Ask(label, defaultValue);
            if (value.Length > 0) return value;
            Console.WriteLine("  A value is required.");
        }
    }

    /// <summary>Asks for a secret without echoing the current one; Enter keeps the current value.</summary>
    public static string AskSecret(string label, string current)
    {
        while (true)
        {
            var value = Ask(current.Length > 0 ? $"{label} (Enter keeps current)" : label);
            if (value.Length > 0) return value;
            if (current.Length > 0) return current;
            Console.WriteLine("  A value is required.");
        }
    }

    public static bool Confirm(string question, bool defaultYes = false)
    {
        var answer = Ask($"{question} ({(defaultYes ? "Y/n" : "y/N")})");
        return answer.Length == 0 ? defaultYes : answer.StartsWith('y') || answer.StartsWith('Y');
    }

    public static T Choose<T>(string label, IReadOnlyList<T> items, Func<T, string> describe)
    {
        for (var i = 0; i < items.Count; i++)
            Console.WriteLine($"  {i + 1}) {describe(items[i])}");
        while (true)
        {
            if (int.TryParse(Ask(label), out var n) && n >= 1 && n <= items.Count) return items[n - 1];
            Console.WriteLine($"  Enter a number from 1 to {items.Count}.");
        }
    }
}
