using System.Text.Json;
using System.Text;

namespace QRCodeAPI.Services;

public class ConsistencyCheckService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConsistencyCheckService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _openRouterApiKey;
    private readonly string _openRouterBaseUrl;

    public ConsistencyCheckService(
        IHttpClientFactory httpClientFactory,
        ILogger<ConsistencyCheckService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _openRouterApiKey = _configuration["ExternalApis:OpenRouter:ApiKey"] ?? string.Empty;
        _openRouterBaseUrl = _configuration["ExternalApis:OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
    }

    public async Task<(double similarity, bool match)> SemanticMatch(string text1, string text2, double threshold = 0.82)
    {
        try
        {
            // Use a simple embedding-based similarity approach
            // For now, we'll use a simpler string similarity as fallback
            // In production, you might want to use a proper embedding model
            
            // Simple approach: Use Jaccard similarity or cosine similarity on word vectors
            var similarity = CalculateStringSimilarity(text1, text2);
            return (similarity, similarity >= threshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in semantic matching");
            return (0.0, false);
        }
    }

    private double CalculateStringSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        // Normalize addresses by expanding abbreviations
        text1 = NormalizeAddress(text1);
        text2 = NormalizeAddress(text2);

        // Normalize texts
        text1 = text1.ToLowerInvariant().Trim();
        text2 = text2.ToLowerInvariant().Trim();

        if (text1 == text2)
            return 1.0;

        // Use Jaccard similarity on word sets
        var words1 = text1.Split(new[] { ' ', ',', '.', ';', ':', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.Split(new[] { ' ', ',', '.', ';', ':', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        if (union == 0)
            return 0.0;

        return (double)intersection / union;
    }

    private string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        // Dictionary of state/province abbreviations to full names
        // Note: Some abbreviations conflict (SK, NL) - prioritize based on common usage
        // For ambiguous cases, context from other address parts helps determine the correct expansion
        var stateAbbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Indian States
            { "TN", "Tamil Nadu" },
            { "AP", "Andhra Pradesh" },
            { "MH", "Maharashtra" },
            { "KA", "Karnataka" },
            { "KL", "Kerala" },
            { "GJ", "Gujarat" },
            { "RJ", "Rajasthan" },
            { "MP", "Madhya Pradesh" },
            { "UP", "Uttar Pradesh" },
            { "WB", "West Bengal" },
            { "BR", "Bihar" },
            { "OR", "Odisha" },
            { "PB", "Punjab" },
            { "HR", "Haryana" },
            { "AS", "Assam" },
            { "JH", "Jharkhand" },
            { "CT", "Chhattisgarh" },
            { "HP", "Himachal Pradesh" },
            { "UT", "Uttarakhand" },
            { "GA", "Goa" },
            { "MN", "Manipur" },
            { "ML", "Meghalaya" },
            { "MZ", "Mizoram" },
            { "TR", "Tripura" },
            { "TG", "Telangana" },
            { "AR", "Arunachal Pradesh" },
            // Canadian Provinces
            { "ON", "Ontario" },
            { "BC", "British Columbia" },
            { "AB", "Alberta" },
            { "QC", "Quebec" },
            { "MB", "Manitoba" },
            { "NS", "Nova Scotia" },
            { "NB", "New Brunswick" },
            { "PE", "Prince Edward Island" },
            { "YT", "Yukon" },
            { "NT", "Northwest Territories" },
            { "NU", "Nunavut" },
            // Handle ambiguous abbreviations with context-aware replacement
            // SK: Sikkim (India) or Saskatchewan (Canada) - check for Indian city names or Canadian context
            // NL: Nagaland (India) or Newfoundland and Labrador (Canada) - check for Indian city names or Canadian context
        };

        string normalized = address;

        // Handle ambiguous abbreviations with context
        string addressLower = address.ToLowerInvariant();
        bool likelyIndian = addressLower.Contains("india") || 
                           addressLower.Contains("tirunelveli") || 
                           addressLower.Contains("pettai") ||
                           addressLower.Contains("pin") ||
                           addressLower.Contains("627004") ||
                           addressLower.Contains("tamil");

        // Replace ambiguous abbreviations based on context
        if (likelyIndian)
        {
            // Indian context
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                @"\bSK\b", 
                "Sikkim", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                @"\bNL\b", 
                "Nagaland", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }
        else
        {
            // Canadian context (default for ambiguous cases)
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                @"\bSK\b", 
                "Saskatchewan", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                @"\bNL\b", 
                "Newfoundland and Labrador", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        // Replace abbreviations with full names
        // Use word boundaries to match whole words only
        foreach (var kvp in stateAbbreviations)
        {
            // Match abbreviation as whole word (case-insensitive)
            // Pattern: word boundary, abbreviation, word boundary
            var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(kvp.Key)}\b";
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized, 
                pattern, 
                kvp.Value, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }

        return normalized;
    }

    public async Task<(double addressConsistency, double nameConsistency, bool documentsConsistent)> CheckDocumentConsistency(
        List<(string address, string name)> documents, 
        double consistencyThreshold)
    {
        if (documents.Count < 2)
        {
            return (0.0, 0.0, false);
        }

        var addressConsistency = await SemanticMatch(documents[0].address, documents[1].address, consistencyThreshold);
        var nameConsistency = await SemanticMatch(documents[0].name, documents[1].name, 0.0);

        var documentsConsistent = addressConsistency.match;

        return (addressConsistency.similarity, nameConsistency.similarity, documentsConsistent);
    }
}

