using System.Text.RegularExpressions;

namespace ComponentCatalogCore.Diagnostics;

public static partial class DiagnosticRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = JsonSecretPattern().Replace(value, "${prefix}<redacted>");
        redacted = AuthorizationPattern().Replace(redacted, "${prefix}${bearer}<redacted>");
        redacted = BearerPattern().Replace(redacted, "Bearer <redacted>");
        return NamedSecretPattern().Replace(redacted, "${prefix}<redacted>");
    }

    [GeneratedRegex("(?<prefix>\\\"(?:authorization|api[_-]?token|access[_-]?token|token|password|keystore(?:password|file)?|storepassword|keypassword)\\\"\\s*:\\s*\\\")[^\\\"]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex("(?<prefix>\\bAuthorization\\s*[:=]\\s*)(?<bearer>Bearer\\s+)?[^\\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex("\\bBearer\\s+[A-Za-z0-9._~+\\/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?<prefix>\\b(?:api[_-]?token|access[_-]?token|token|password|keystore(?:password|file)?|storepassword|keypassword)\\b\\s*[:=]\\s*)(?:[\\\"'])?[^\\s,;\\\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretPattern();
}
