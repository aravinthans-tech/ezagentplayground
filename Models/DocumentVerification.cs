namespace QRCodeAPI.Models;

public class DocumentVerification
{
    public int DocumentIndex { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public int PageIndex { get; set; }
    //Aravinthan changes
    public string ExtractedAddress { get; set; } = string.Empty;
    public string ExtractedName { get; set; } = string.Empty;
    public double SimilarityToExpected { get; set; }
    public bool AddressMatch { get; set; }
    public bool GoogleMapsVerified { get; set; }
    public string GoogleMapsFormattedAddress { get; set; } = string.Empty;
    public double AuthenticityScore { get; set; }
    public Dictionary<string, object> ExtractedFields { get; set; } = new Dictionary<string, object>();
}

