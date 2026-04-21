using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

public class FormAllSortBy
{
    [JsonPropertyName("criteria")]
    public string? Criteria { get; set; }

    [JsonPropertyName("order")]
    public string? Order { get; set; }
}

public class FormAllFilter
{
    [JsonPropertyName("criteria")]
    public string? Criteria { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class FormAllFilterGroup
{
    [JsonPropertyName("groupCondition")]
    public string? GroupCondition { get; set; }

    [JsonPropertyName("filters")]
    public List<FormAllFilter>? Filters { get; set; }
}

/// <summary>
/// Body shape for POST https://eztapi.ezofis.com/api/form/all (plaintext before encryptAES).
/// </summary>
public class FormAllQueryPayload
{
    [JsonPropertyName("sortBy")]
    public FormAllSortBy? SortBy { get; set; }

    [JsonPropertyName("groupBy")]
    public string? GroupBy { get; set; }

    [JsonPropertyName("filterBy")]
    public List<FormAllFilterGroup>? FilterBy { get; set; }

    [JsonPropertyName("currentPage")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}
