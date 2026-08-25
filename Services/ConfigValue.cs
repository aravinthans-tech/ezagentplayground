namespace QRCodeAPI.Services;

/// <summary>
/// Resolves config from nested appsettings keys and common deploy secret short names.
/// ASP.NET maps env <c>ExternalApis__AwsRekognition__AccessKey</c> → <c>ExternalApis:AwsRekognition:AccessKey</c>.
/// Hosting UIs sometimes use short names like <c>GoogleMapsApiKey</c> instead.
/// </summary>
internal static class ConfigValue
{
    public static string Get(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var value = Clean(configuration[key]);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the first source where both related keys are non-empty (avoids mixing AccessKey from one env with SecretKey from another).
    /// Each entry is (accessKeyPath, secretKeyPath).
    /// </summary>
    public static (string AccessKey, string SecretKey, string Source) GetPair(
        IConfiguration configuration,
        params (string AccessKey, string SecretKey)[] sources)
    {
        foreach (var (accessPath, secretPath) in sources)
        {
            var access = Clean(configuration[accessPath]);
            var secret = Clean(configuration[secretPath]);
            if (!string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(secret))
                return (access, secret, accessPath);
        }

        return (string.Empty, string.Empty, string.Empty);
    }

    /// <summary>Trim whitespace and strip one layer of wrapping quotes from secret UIs.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var v = value.Trim();
        if (v.Length >= 2 &&
            ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            v = v[1..^1].Trim();
        }

        return v;
    }
}
