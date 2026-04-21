using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

public class SubFormDetailsApiRequest
{
    [JsonPropertyName("formId")]
    public int FormId { get; set; }
}
