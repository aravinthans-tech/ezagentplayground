using System.Text.Json;
using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

public class SubFormSubmitAndArchiveApiRequest
{
    [JsonPropertyName("formId")]
    public int FormId { get; set; }

    [JsonPropertyName("templateId")]
    public int TemplateId { get; set; }

    [JsonPropertyName("entryIdForFormEntry")]
    public int EntryIdForFormEntry { get; set; } = 0;

    [JsonPropertyName("entryId")]
    public int EntryId { get; set; } = 0;

    [JsonPropertyName("isMultiple")]
    public bool IsMultiple { get; set; }

    [JsonPropertyName("panelName")]
    public string? PanelName { get; set; }

    [JsonPropertyName("fields")]
    public JsonElement Fields { get; set; }
}
