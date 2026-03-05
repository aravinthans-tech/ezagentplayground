namespace QRCodeAPI.Models;

public class FaceMatchResult
{
    public bool Match { get; set; }
    public int MatchScore { get; set; }
    public string Message { get; set; } = string.Empty;
    public byte[]? LicenseFaceImage { get; set; }
    public byte[]? SelfieFaceImage { get; set; }
}

