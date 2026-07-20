namespace V6Playground.Configuration;

public sealed class V6ApiOptions
{
    public const string SectionName = "V6Api";

    /// <summary>Hosted V6 API base URL, e.g. https://demo.ezofis.com/v6api</summary>
    public string BaseUrl { get; set; } = "https://demo.ezofis.com/v6api";
}
