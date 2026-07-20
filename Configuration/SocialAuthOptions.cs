namespace V6Playground.Configuration;

public sealed class SocialAuthOptions
{
    public const string SectionName = "SocialAuth";

    public string GoogleClientId { get; set; } = "";
    public string MsalClientId { get; set; } = "";
}
