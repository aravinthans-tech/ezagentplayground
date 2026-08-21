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
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
