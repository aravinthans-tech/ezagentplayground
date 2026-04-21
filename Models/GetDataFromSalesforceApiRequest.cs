using System.Text.Json;
using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

public class GetDataFromSalesforceApiRequest
{
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}
