using System.Security.Cryptography;
using System.Text;

namespace Gdup.Config;

/// <summary>Encrypts config secrets for the current Windows user.</summary>
public static class Dpapi
{
    public const string Prefix = "dpapi:";

    public static string Protect(string plain) =>
        Prefix + Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]), null, DataProtectionScope.CurrentUser))
            : stored;
}
