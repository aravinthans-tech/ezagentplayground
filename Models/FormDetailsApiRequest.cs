using System.Text.Json.Serialization;

namespace QRCodeAPI.Models;

/// <summary>
/// Request body for POST /api/FormDetails.
/// </summary>
public class FormDetailsApiRequest
{
    [JsonPropertyName("query")]
    public FormAllQueryPayload? Query { get; set; }

    /// <summary>
    /// When true, encrypt the serialized query via encryptAES and send { "encryptedPayload": "..." } to form/all.
    /// </summary>
    [JsonPropertyName("useEncryptedRequest")]
    public bool UseEncryptedRequest { get; set; } = true;

    /// <summary>
    /// When true, encrypt form/all JSON response via encryptAES before returning in EncryptOutput.
    /// </summary>
    [JsonPropertyName("useEncryptedResponse")]
    public bool UseEncryptedResponse { get; set; } = true;
}
