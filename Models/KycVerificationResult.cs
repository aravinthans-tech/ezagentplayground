namespace QRCodeAPI.Models;

public class KycVerificationResult
{
    public bool FinalResult { get; set; }
    public double AddressConsistencyScore { get; set; }
    public double NameConsistencyScore { get; set; }
    public double DocumentConsistencyScore { get; set; }
    public double AverageAuthenticityScore { get; set; }
    public bool DocumentsConsistent { get; set; }
    public List<DocumentVerification> Documents { get; set; } = new List<DocumentVerification>();
    public FaceMatchResult? FaceMatch { get; set; }
    public string StatusHtml { get; set; } = string.Empty;
    public string VerificationTableHtml { get; set; } = string.Empty;
    public Dictionary<string, Dictionary<string, object>> ExtractedFields { get; set; } = new Dictionary<string, Dictionary<string, object>>();
}

