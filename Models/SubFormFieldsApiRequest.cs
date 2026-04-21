using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

public class SubFormFieldsApiRequest
{
    [JsonPropertyName("formId")]
    public int FormId { get; set; }

    [JsonPropertyName("subforms")]
    public List<string>? Subforms { get; set; }
}
