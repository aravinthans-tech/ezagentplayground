using QRCodeAPI.Models;
using System.Text;

namespace QRCodeAPI.Services;

public class KycVerificationService
{
    private readonly DocumentProcessingService _documentProcessingService;
    private readonly AddressVerificationService _addressVerificationService;
    private readonly ConsistencyCheckService _consistencyCheckService;
    private readonly FaceMatchingService _faceMatchingService;
    private readonly ILogger<KycVerificationService> _logger;

    public KycVerificationService(
        DocumentProcessingService documentProcessingService,
        AddressVerificationService addressVerificationService,
        ConsistencyCheckService consistencyCheckService,
        FaceMatchingService faceMatchingService,
        ILogger<KycVerificationService> logger)
    {
        _documentProcessingService = documentProcessingService;
        _addressVerificationService = addressVerificationService;
        _consistencyCheckService = consistencyCheckService;
        _faceMatchingService = faceMatchingService;
        _logger = logger;
    }

    public async Task<KycVerificationResult> VerifyKyc(KycVerificationRequest request)
    {
        var result = new KycVerificationResult();
        var documents = new List<DocumentVerification>();
        var addresses = new List<string>();
        var names = new List<string>();
        var authenticityScores = new List<double>();

        try
        {
            if (request.Documents == null || request.Documents.Count < 2)
            {
                result.StatusHtml = "❌ <b style='color:red;'>Please upload at least two documents.</b>";
                return result;
            }

            // Start face matching in parallel (if provided) - doesn't depend on document processing
            Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)>? faceMatchTask = null;
            if (request.LicenseImage != null && request.SelfieImage != null)
            {
                faceMatchTask = _faceMatchingService.ProcessAndCompare(request.LicenseImage, request.SelfieImage);
            }

            // Phase 1: Process all documents in parallel to extract addresses (skip Google Maps verification)
            var documentTasks = new List<Task<DocumentVerification>>();
            for (int idx = 0; idx < request.Documents.Count; idx++)
            {
                var document = request.Documents[idx];
                var documentIndex = idx + 1;
                documentTasks.Add(ProcessDocumentAsync(document, documentIndex, request.ModelChoice, request.ExpectedAddress, request.ConsistencyThreshold, skipGoogleMapsVerification: true));
            }

            // Wait for all documents to be processed
            var processedDocuments = await Task.WhenAll(documentTasks);
            documents.AddRange(processedDocuments);

            // Extract addresses and names for consistency check
            foreach (var doc in documents)
            {
                addresses.Add(doc.ExtractedAddress ?? string.Empty);
                names.Add(doc.ExtractedName ?? string.Empty);
                authenticityScores.Add(doc.AuthenticityScore);
            }

            // Phase 2: Check consistency between documents
            var documentPairs = addresses.Zip(names, (addr, name) => (addr, name)).ToList();
            var (addressConsistency, nameConsistency, documentsConsistent) = 
                await _consistencyCheckService.CheckDocumentConsistency(documentPairs, request.ConsistencyThreshold);

            // Phase 3: Conditionally verify with Google Maps only if addresses match
            if (documentsConsistent)
            {
                // Addresses match - verify each document with Google Maps in parallel
                var verificationTasks = documents.Select(doc => VerifyAddressWithGoogleMapsAsync(doc)).ToList();
                await Task.WhenAll(verificationTasks);
                
                // Update authenticity scores after Google Maps verification
                authenticityScores.Clear();
                foreach (var doc in documents)
                {
                    authenticityScores.Add(doc.AuthenticityScore);
                }
            }
            else
            {
                // Addresses don't match - skip Google Maps verification, mark as not verified
                foreach (var doc in documents)
                {
                    doc.GoogleMapsVerified = false;
                    doc.GoogleMapsFormattedAddress = doc.ExtractedAddress ?? string.Empty;
                }
                _logger.LogInformation("Addresses from documents do not match. Skipping Google Maps verification.");
            }

            result.AddressConsistencyScore = addressConsistency;
            result.NameConsistencyScore = nameConsistency;
            result.DocumentConsistencyScore = addressConsistency;
            result.DocumentsConsistent = documentsConsistent;
            result.AverageAuthenticityScore = authenticityScores.Any() ? authenticityScores.Average() : 0.0;
            result.Documents = documents;

            // Await face matching result (if it was started)
            if (faceMatchTask != null)
            {
                var (licenseFace, selfieFace, match, matchScore, message) = await faceMatchTask;
                
                result.FaceMatch = new FaceMatchResult
                {
                    Match = match,
                    MatchScore = matchScore,
                    Message = message,
                    LicenseFaceImage = licenseFace,
                    SelfieFaceImage = selfieFace
                };
            }

            // Calculate final result
            var allAddressesMatch = documents.All(d => d.AddressMatch && d.GoogleMapsVerified);
            var addressAndConsistencyPass = allAddressesMatch && (addressConsistency >= request.ConsistencyThreshold);
            
            // If face matching was performed, it must also pass for overall verification to pass
            bool faceMatchPass = true; // Default to true if no face matching was performed
            if (result.FaceMatch != null)
            {
                faceMatchPass = result.FaceMatch.Match; // Face match must pass if it was performed
            }
            
            result.FinalResult = addressAndConsistencyPass && faceMatchPass;

            // Generate status HTML with matching page styling
            var statusColor = result.FinalResult ? "text-green-600" : "text-red-600";
            var borderColor = result.FinalResult ? "border-green-600" : "border-red-600";
            var statusIcon = result.FinalResult ? "✅" : "❌";
            var statusText = result.FinalResult ? "Verification Passed" : "Verification Failed";
            
            result.StatusHtml = $@"
                <div class=""bg-white rounded-xl shadow-lg p-4 border-2 {borderColor} animate-fadeInUp"">
                    <h4 class=""text-lg font-semibold {statusColor} mb-3"">{statusIcon} {statusText}</h4>
                    <div class=""grid grid-cols-2 md:grid-cols-4 gap-3 text-xs"">
                        <div class=""bg-gray-50 rounded-lg p-2"">
                            <div class=""text-gray-600 font-medium mb-1"">Address Consistency</div>
                            <div class=""text-gray-900 font-bold text-sm"">{Math.Round(addressConsistency * 100)}%</div>
                        </div>
                        <div class=""bg-gray-50 rounded-lg p-2"">
                            <div class=""text-gray-600 font-medium mb-1"">Name Consistency</div>
                            <div class=""text-gray-900 font-bold text-sm"">{Math.Round(nameConsistency * 100)}%</div>
                        </div>
                        <div class=""bg-gray-50 rounded-lg p-2"">
                            <div class=""text-gray-600 font-medium mb-1"">Overall Consistency</div>
                            <div class=""text-gray-900 font-bold text-sm"">{Math.Round(addressConsistency * 100)}%</div>
                        </div>
                        <div class=""bg-gray-50 rounded-lg p-2"">
                            <div class=""text-gray-600 font-medium mb-1"">Authenticity Score</div>
                            <div class=""text-gray-900 font-bold text-sm"">{Math.Round(result.AverageAuthenticityScore * 100)}%</div>
                        </div>
                    </div>
                </div>";

            // Generate verification table HTML
            result.VerificationTableHtml = FormatVerificationTable(result);

            // Store extracted fields
            for (int i = 0; i < documents.Count; i++)
            {
                result.ExtractedFields[$"document_{i + 1}"] = documents[i].ExtractedFields;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in KYC verification");
            result.StatusHtml = $"❌ <b style='color:red;'>Error: {ex.Message}</b>";
            return result;
        }
    }

    private async Task<DocumentVerification> ProcessDocumentAsync(
        IFormFile document,
        int documentIndex,
        string modelChoice,
        string expectedAddress,
        double consistencyThreshold,
        bool skipGoogleMapsVerification = false)
    {
        var docVerification = new DocumentVerification { DocumentIndex = documentIndex };

        // Step 1: Extract text using OCR (must be done first)
        var text = await _documentProcessingService.ExtractTextFromFile(document);

        // Step 2: Extract KYC fields and address in parallel (both use LLM but different prompts)
        var kycFieldsTask = _documentProcessingService.ExtractKycFields(text, modelChoice);
        var addressTask = _documentProcessingService.ExtractAddressWithLLM(text, modelChoice);

        await Task.WhenAll(kycFieldsTask, addressTask);

        var kycFields = await kycFieldsTask;
        var address = await addressTask;

        docVerification.ExtractedFields = kycFields;
        docVerification.ExtractedName = kycFields.GetValueOrDefault("full_name", "Not provided")?.ToString() ?? "Not provided";

        // If address extraction failed, try to get it from KYC fields as fallback
        if (string.IsNullOrWhiteSpace(address) || address.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            var addressFromKyc = kycFields.GetValueOrDefault("address")?.ToString();
            if (!string.IsNullOrWhiteSpace(addressFromKyc) && !addressFromKyc.Equals("Not provided", StringComparison.OrdinalIgnoreCase))
            {
                address = addressFromKyc;
                _logger.LogInformation("Using address from KYC fields as fallback for document {Index}", documentIndex);
            }
        }

        docVerification.ExtractedAddress = address;

        // Step 3: Check similarity to expected address
        var (similarity, match) = await _consistencyCheckService.SemanticMatch(address, expectedAddress, consistencyThreshold);
        docVerification.SimilarityToExpected = similarity;
        docVerification.AddressMatch = match;

        // Step 4: Conditionally verify address with Google Maps
        if (skipGoogleMapsVerification)
        {
            // Skip Google Maps verification, set defaults
            docVerification.GoogleMapsVerified = false;
            docVerification.GoogleMapsFormattedAddress = address;
            // Calculate authenticity score using original address
            var (authScore, _) = await _consistencyCheckService.SemanticMatch(address, address);
            docVerification.AuthenticityScore = authScore;
        }
        else
        {
            // Verify address with Google Maps
            var (verified, formattedAddress) = await _addressVerificationService.VerifyAddress(address);
            docVerification.GoogleMapsVerified = verified;
            docVerification.GoogleMapsFormattedAddress = formattedAddress;
            
            // Calculate authenticity score (similarity between extracted address and Google Maps formatted address)
            var (authScore, _) = await _consistencyCheckService.SemanticMatch(address, formattedAddress);
            docVerification.AuthenticityScore = authScore;
        }

        return docVerification;
    }

    private async Task VerifyAddressWithGoogleMapsAsync(DocumentVerification document)
    {
        if (string.IsNullOrWhiteSpace(document.ExtractedAddress))
        {
            document.GoogleMapsVerified = false;
            document.GoogleMapsFormattedAddress = document.ExtractedAddress ?? string.Empty;
            return;
        }

        var (verified, formattedAddress) = await _addressVerificationService.VerifyAddress(document.ExtractedAddress);
        document.GoogleMapsVerified = verified;
        document.GoogleMapsFormattedAddress = formattedAddress;

        // Update authenticity score based on Google Maps verification
        var (authScore, _) = await _consistencyCheckService.SemanticMatch(document.ExtractedAddress, formattedAddress);
        document.AuthenticityScore = authScore;
    }

    private string FormatVerificationTable(KycVerificationResult result)
    {
        var html = new StringBuilder();
        html.AppendLine(@"<div class=""bg-white rounded-xl shadow-lg p-4 border-2 border-cyan-200 animate-fadeInUp mb-4"">");
        html.AppendLine(@"  <h5 class=""text-sm font-semibold text-gray-900 mb-3"">Document Details</h5>");
        
        foreach (var doc in result.Documents)
        {
            var addressMatchColor = doc.AddressMatch ? "text-green-600" : "text-red-600";
            var mapsVerifiedColor = doc.GoogleMapsVerified ? "text-green-600" : "text-red-600";
            var similarityColor = doc.SimilarityToExpected >= 0.7 ? "text-green-600" : doc.SimilarityToExpected >= 0.5 ? "text-yellow-600" : "text-red-600";
            
            html.AppendLine($@"  <div class=""mb-4 p-3 bg-gray-50 rounded-lg border border-gray-200"">");
            html.AppendLine($@"    <h6 class=""text-xs font-bold text-gray-700 mb-2"">Document {doc.DocumentIndex}</h6>");
            html.AppendLine($@"    <div class=""grid grid-cols-1 md:grid-cols-2 gap-2 text-xs"">");
            
            // Address
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Address</div>");
            html.AppendLine($@"        <div class=""text-gray-900 text-xs break-words"">{System.Net.WebUtility.HtmlEncode(doc.ExtractedAddress ?? "None")}</div>");
            html.AppendLine($@"      </div>");
            
            // Full Name
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Full Name</div>");
            html.AppendLine($@"        <div class=""text-gray-900 text-xs"">{System.Net.WebUtility.HtmlEncode(doc.ExtractedName ?? "None")}</div>");
            html.AppendLine($@"      </div>");
            
            // Address Similarity
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Address Similarity</div>");
            html.AppendLine($@"        <div class=""{similarityColor} font-bold text-xs"">{Math.Round(doc.SimilarityToExpected * 100)}%</div>");
            html.AppendLine($@"      </div>");
            
            // Address Match
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Address Match</div>");
            html.AppendLine($@"        <div class=""{addressMatchColor} font-bold text-xs"">{(doc.AddressMatch ? "Yes" : "No")}</div>");
            html.AppendLine($@"      </div>");
            
            // Google Maps Verified
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Google Maps Verified</div>");
            html.AppendLine($@"        <div class=""{mapsVerifiedColor} font-bold text-xs"">{(doc.GoogleMapsVerified ? "Yes" : "No")}</div>");
            html.AppendLine($@"      </div>");
            
            // Authenticity Score
            html.AppendLine($@"      <div class=""bg-white rounded p-2"">");
            html.AppendLine($@"        <div class=""text-gray-600 font-medium mb-1"">Authenticity Score</div>");
            html.AppendLine($@"        <div class=""text-gray-900 font-bold text-xs"">{Math.Round(doc.AuthenticityScore * 100)}%</div>");
            html.AppendLine($@"      </div>");
            
            html.AppendLine($@"    </div>");
            html.AppendLine($@"  </div>");
        }

        html.AppendLine(@"</div>");
        return html.ToString();
    }
}

