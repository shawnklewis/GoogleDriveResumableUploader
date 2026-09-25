namespace Gdup.Cli;

/// <summary>Bad arguments or configuration; reported without a stack trace, exit code 1.</summary>
public sealed class UsageException(string message) : Exception(message);
