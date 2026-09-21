namespace V6Playground.Configuration;

public sealed class V6ApiOptions
{
    public const string SectionName = "V6Api";

    /// <summary>Hosted V6 API base URL, e.g. https://cloud.ezofis.com</summary>
    public string BaseUrl { get; set; } = "https://cloud.ezofis.com";
}
