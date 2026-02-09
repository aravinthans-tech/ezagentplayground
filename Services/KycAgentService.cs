using QRCodeAPI.Models;
// using Leadtools;
// using Leadtools.Codecs;
// using Leadtools.Ocr;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace QRCodeAPI.Services;

public class KycAgentService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KycAgentService> _logger;
    private readonly string _apiContentRootPath;
    private readonly FaceMatchingService? _faceMatchingService;

    public KycAgentService(IConfiguration configuration, ILogger<KycAgentService> logger, FaceMatchingService? faceMatchingService = null)
    {
        _configuration = configuration;
        _logger = logger;
        _apiContentRootPath = _configuration["License:ContentRootPath"] ?? AppDomain.CurrentDomain.BaseDirectory;
        _faceMatchingService = faceMatchingService;
    }

    // private bool InitLead()
    // {
    //     return QrCodeService.SetLicense(false, _apiContentRootPath);
    // }

    public async Task<ResultForHttpsCode> ProcessDocument(IFormFile file)
    {
        var result = new ResultForHttpsCode();

        try
        {
            if (file == null || file.Length == 0)
            {
                result.id = 0;
                result.EncryptOutput = "File is required";
                return result;
            }

            // Initialize LEADTOOLS
            // if (!InitLead())
            // {
            //     result.id = 0;
            //     result.EncryptOutput = "LEADTOOLS license initialization failed";
            //     return result;
            // }

            // Extract text from document
            string extractedText = await ExtractTextFromDocument(file);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                result.id = 0;
                result.EncryptOutput = "Could not extract text from document. Please ensure the document is clear and readable.";
                return result;
            }

            // Extract KYC fields from text
            var kycData = ExtractKycFields(extractedText);

            // Generate HTML report
            string htmlReport = GenerateHtmlReport(kycData, extractedText);

            result.id = 1;
            result.output = htmlReport;
            result.EncryptOutput = null;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing KYC document");
            result.id = 0;
            result.EncryptOutput = "ERROR CODE:WDBR740F300DB30 " + ex.Message;
            return result;
        }
    }

    public async Task<ResultForHttpsCode> ProcessMultipleDocuments(
        List<IFormFile> documents, 
        IFormFile? licenseImage = null, 
        IFormFile? selfieImage = null)
    {
        var result = new ResultForHttpsCode();

        try
        {
            if (documents == null || documents.Count == 0)
            {
                result.id = 0;
                result.EncryptOutput = "At least one document is required";
                return result;
            }

            // Initialize LEADTOOLS
            // if (!InitLead())
            // {
            //     result.id = 0;
            //     result.EncryptOutput = "LEADTOOLS license initialization failed";
            //     return result;
            // }

            // Process each document
            var allKycData = new List<(KycData data, int index)>();
            for (int i = 0; i < documents.Count; i++)
            {
                var doc = documents[i];
                if (doc == null || doc.Length == 0)
                    continue;

                try
                {
                    string extractedText = await ExtractTextFromDocument(doc);
                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        var kycData = ExtractKycFields(extractedText);
                        allKycData.Add((kycData, i + 1));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing document {Index}", i + 1);
                }
            }

            if (allKycData.Count == 0)
            {
                result.id = 0;
                result.EncryptOutput = "Could not extract text from any document. Please ensure the documents are clear and readable.";
                return result;
            }

            // Perform face matching if both images provided
            string? faceMatchHtml = null;
            if (licenseImage != null && selfieImage != null && _faceMatchingService != null)
            {
                try
                {
                    var (licenseFace, selfieFace, match, matchScore, message) = await _faceMatchingService.ProcessAndCompare(licenseImage, selfieImage);
                    faceMatchHtml = GenerateFaceMatchHtml(match, matchScore, message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error performing face matching");
                    faceMatchHtml = "<div style='color: #dc2626; padding: 12px; background: #fee2e2; border-radius: 8px; margin: 14px 0;'>⚠️ Face matching failed: " + EscapeHtml(ex.Message) + "</div>";
                }
            }

            // Generate combined HTML report
            string htmlReport = GenerateMultipleDocumentsHtmlReport(allKycData, faceMatchHtml);

            result.id = 1;
            result.output = htmlReport;
            result.EncryptOutput = null;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing multiple KYC documents");
            result.id = 0;
            result.EncryptOutput = "ERROR CODE:WDBR740F300DB30 " + ex.Message;
            return result;
        }
    }

    private async Task<string> ExtractTextFromDocument(IFormFile file)
    {
        try
        {
            using (var stream = file.OpenReadStream())
            {
                // Save to temporary file for LEADTOOLS processing
                string tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(file.FileName));
                
                try
                {
                    using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await stream.CopyToAsync(fileStream);
                    }

                    // Use LEADTOOLS OCR to extract text
                    // using (RasterCodecs codecs = new RasterCodecs())
                    // {
                    //     // Create OCR engine
                    //     using (IOcrEngine ocrEngine = OcrEngineManager.CreateEngine(OcrEngineType.LEAD))
                    //     {
                    //         // Start the OCR engine
                    //         ocrEngine.Startup(codecs, null, null, null);

                    //         try
                    //         {
                    //             // Create OCR document from file
                    //             using (IOcrDocument ocrDocument = ocrEngine.DocumentManager.CreateDocument())
                    //             {
                    //                 // Add page from file
                    //                 IOcrPage ocrPage = ocrDocument.Pages.AddPage(tempFilePath, null);
                    //                 
                    //                 // Recognize the page
                    //                 ocrPage.Recognize(null);

                    //                 // Get recognized text
                    //                 string extractedText = ocrPage.GetText(-1);
                    //                 
                    //                 return extractedText ?? string.Empty;
                    //             }
                    //         }
                    //         catch (Exception ocrEx)
                    //         {
                    //             _logger.LogWarning(ocrEx, "OCR recognition failed, trying fallback method");
                    //             
                    //             // Fallback: Try to load as image and return placeholder
                    //             RasterImage image = codecs.Load(tempFilePath, 0, CodecsLoadByteOrder.BgrOrGray, 1, 1);
                    //             if (image != null)
                    //             {
                    //                 image.Dispose();
                    //                 // Return a message indicating OCR failed but file was readable
                    //                 return "OCR processing encountered an issue. Please ensure the document is clear and readable.";
                    //             }
                    //             
                    //             return string.Empty;
                    //         }
                    //         finally
                    //         {
                    //             ocrEngine.Shutdown();
                    //         }
                    //     }
                    // }
                    
                    // Return empty string since OCR is disabled
                    return string.Empty;
                }
                finally
                {
                    // Clean up temporary file
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete temporary file: {FilePath}", tempFilePath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from document");
            throw;
        }
    }

    private KycData ExtractKycFields(string text)
    {
        var kycData = new KycData();

        if (string.IsNullOrWhiteSpace(text))
            return kycData;

        // Detect Document Type first (needed for specific extraction)
        kycData.DocumentType = DetectDocumentType(text);

        // If still unknown, try to detect based on number patterns
        if (kycData.DocumentType == "Unknown")
        {
            // Check for Aadhar pattern (12 digits)
            if (Regex.IsMatch(text, @"\d{4}\s?\d{4}\s?\d{4}") || Regex.IsMatch(text, @"\d{12}"))
            {
                kycData.DocumentType = "Aadhar Card";
            }
            // Check for PAN pattern (10 alphanumeric)
            else if (Regex.IsMatch(text, @"[A-Z]{5}\d{4}[A-Z]", RegexOptions.IgnoreCase))
            {
                kycData.DocumentType = "PAN Card";
            }
        }

        // Extract Full Name
        kycData.FullName = ExtractName(text);

        // Extract ID/Passport/Aadhar/PAN Number
        kycData.IdNumber = ExtractIdNumber(text, kycData.DocumentType);

        // Extract Date of Birth
        kycData.DateOfBirth = ExtractDateOfBirth(text);

        // Extract Address
        kycData.Address = ExtractAddress(text);

        // Extract Nationality
        kycData.Nationality = ExtractNationality(text);

        // Extract Expiry Date
        kycData.ExpiryDate = ExtractExpiryDate(text);

        // Extract additional fields for Indian documents
        if (kycData.DocumentType == "PAN Card")
        {
            kycData.FatherName = ExtractFatherName(text);
        }
        else if (kycData.DocumentType == "Aadhar Card")
        {
            kycData.Gender = ExtractGender(text);
        }

        return kycData;
    }

    private string ExtractName(string text)
    {
        // Patterns for name extraction (including Indian document formats)
        var patterns = new[]
        {
            // Standard patterns
            @"(?:Name|Full Name|NAME|FULL NAME|Name of Applicant|NAME OF APPLICANT)[\s:]+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)",
            @"(?:Name|Full Name|NAME|FULL NAME|Name of Applicant)[\s:]+([A-Z\s]{3,50})",
            // Indian document patterns (often in all caps)
            @"(?:Name|NAME)[\s:]+([A-Z\s]{5,50})",
            // Pattern for lines that start with name-like text
            @"^([A-Z][a-z]+\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
            // Pattern for all caps names (common in Indian documents)
            @"(?:Name|NAME)[\s:]+([A-Z]{2,}\s+[A-Z]{2,}(?:\s+[A-Z]{2,})?)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                string name = match.Groups[1].Value.Trim();
                // Clean up extra whitespace
                name = Regex.Replace(name, @"\s+", " ");
                if (name.Length > 3 && name.Length < 100) // Reasonable name length
                {
                    return name;
                }
            }
        }

        return "Not Found";
    }

    private string ExtractIdNumber(string text, string documentType)
    {
        // Aadhar Card: 12-digit number
        if (documentType == "Aadhar Card")
        {
            var aadharPatterns = new[]
            {
                @"(?:Aadhaar|Aadhar|AADHAAR|AADHAR|Aadhaar No|Aadhaar Number|आधार)[\s:]+(\d{4}\s?\d{4}\s?\d{4})",
                @"(?:Aadhaar|Aadhar|AADHAAR|AADHAR|आधार)[\s:]+(\d{12})",
                @"(\d{4}\s?\d{4}\s?\d{4})", // Format: XXXX XXXX XXXX or XXXXXXXXXXXX
                @"(\d{12})", // Direct 12-digit match
            };

            foreach (var pattern in aadharPatterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (match.Success && match.Groups.Count > 1)
                {
                    string aadhar = match.Groups[1].Value.Trim().Replace(" ", "").Replace("-", "");
                    if (aadhar.Length == 12 && Regex.IsMatch(aadhar, @"^\d{12}$"))
                    {
                        // Format as XXXX XXXX XXXX for display
                        return $"{aadhar.Substring(0, 4)} {aadhar.Substring(4, 4)} {aadhar.Substring(8, 4)}";
                    }
                }
            }

            // Fallback: Find any 12-digit number in the text (even without label)
            var fallbackMatch = Regex.Match(text, @"(\d{4}\s?\d{4}\s?\d{4})");
            if (fallbackMatch.Success)
            {
                string aadhar = fallbackMatch.Groups[1].Value.Trim().Replace(" ", "").Replace("-", "");
                if (aadhar.Length == 12 && Regex.IsMatch(aadhar, @"^\d{12}$"))
                {
                    return $"{aadhar.Substring(0, 4)} {aadhar.Substring(4, 4)} {aadhar.Substring(8, 4)}";
                }
            }
        }

        // PAN Card: 10-character alphanumeric (ABCDE1234F format)
        if (documentType == "PAN Card")
        {
            var panPatterns = new[]
            {
                @"(?:PAN|Permanent Account Number|PAN Number|PAN NO)[\s:]+([A-Z]{5}\d{4}[A-Z])",
                @"(?:PAN|Permanent Account Number)[\s:]+([A-Z]{5}\s?\d{4}\s?[A-Z])",
                @"([A-Z]{5}\d{4}[A-Z])", // Format: ABCDE1234F
            };

            foreach (var pattern in panPatterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (match.Success && match.Groups.Count > 1)
                {
                    string pan = match.Groups[1].Value.Trim().Replace(" ", "").ToUpper();
                    if (pan.Length == 10 && Regex.IsMatch(pan, @"^[A-Z]{5}\d{4}[A-Z]$"))
                    {
                        return pan;
                    }
                }
            }
        }

        // Generic patterns for ID/Passport number extraction
        var patterns = new[]
        {
            @"(?:ID|Passport|PASSPORT|ID Number|ID NO|IDNO)[\s:]+([A-Z0-9]{6,20})",
            @"(?:ID|Passport|PASSPORT)[\s#:]+([A-Z]{1,2}[0-9]{6,12})",
            @"([A-Z]{1,3}[0-9]{6,15})", // Generic pattern for alphanumeric IDs
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                string id = match.Groups[1].Value.Trim();
                if (id.Length >= 6 && id.Length <= 20)
                {
                    return id;
                }
            }
        }

        return "Not Found";
    }

    private string ExtractDateOfBirth(string text)
    {
        // Patterns for date of birth
        var patterns = new[]
        {
            @"(?:DOB|Date of Birth|Birth Date|DATE OF BIRTH|BIRTH DATE)[\s:]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})",
            @"(?:DOB|Date of Birth|Birth Date)[\s:]+(\d{1,2}\s+\w+\s+\d{4})",
            @"(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})", // Generic date pattern
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return "Not Found";
    }

    private string ExtractAddress(string text)
    {
        // Patterns for address extraction (including Indian address formats)
        var patterns = new[]
        {
            @"(?:Address|ADDRESS|Address of Applicant|ADDRESS OF APPLICANT)[\s:]+([A-Z0-9\s,.-]{10,150})",
            @"(?:Address|ADDRESS)[\s:]+(.+?)(?:\n\n|\n[A-Z]{2,}:|$)",
            // Indian address patterns (often multi-line)
            @"(?:Address|ADDRESS)[\s:]+([A-Z0-9\s,.-/]{10,200})",
            // Pattern for address lines (common in Indian documents)
            @"(?:Address|ADDRESS)[\s:]+((?:[A-Z0-9\s,.-]+(?:\n|$)){1,5})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (match.Success && match.Groups.Count > 1)
            {
                string address = match.Groups[1].Value.Trim();
                // Clean up address (remove extra whitespace, newlines)
                address = Regex.Replace(address, @"\s+", " ");
                address = Regex.Replace(address, @"\n+", ", ");
                if (address.Length > 10 && address.Length < 250)
                {
                    return address;
                }
            }
        }

        return "Not Found";
    }

    private string ExtractNationality(string text)
    {
        // Patterns for nationality
        var patterns = new[]
        {
            @"(?:Nationality|Country|NATIONALITY|COUNTRY)[\s:]+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
            @"(?:Nationality|Country)[\s:]+([A-Z]{2,3})", // Country codes
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return "Not Found";
    }

    private string ExtractExpiryDate(string text)
    {
        // Patterns for expiry date
        var patterns = new[]
        {
            @"(?:Expiry|Expires|Valid Until|EXPIRY|EXPIRES)[\s:]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4})",
            @"(?:Expiry|Expires|Valid Until)[\s:]+(\d{1,2}\s+\w+\s+\d{4})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return "Not Found";
    }

    private string ExtractFatherName(string text)
    {
        // Patterns for Father's Name (common in PAN cards)
        var patterns = new[]
        {
            @"(?:Father|Father's Name|FATHER|FATHER'S NAME|Father Name)[\s:]+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)",
            @"(?:Father|Father's Name|FATHER)[\s:]+([A-Z\s]+)",
            @"(?:Father|FATHER)[\s:]+([A-Z][A-Z\s]{5,50})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                string fatherName = match.Groups[1].Value.Trim();
                // Clean up extra whitespace
                fatherName = Regex.Replace(fatherName, @"\s+", " ");
                if (fatherName.Length > 3 && fatherName.Length < 100)
                {
                    return fatherName;
                }
            }
        }

        return "Not Found";
    }

    private string ExtractGender(string text)
    {
        // Patterns for Gender (common in Aadhar cards)
        var patterns = new[]
        {
            @"(?:Gender|Sex|GENDER|SEX)[\s:]+(Male|Female|MALE|FEMALE|M|F|M/F)",
            @"(?:Gender|Sex)[\s:]+([MF])",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                string gender = match.Groups[1].Value.Trim().ToUpper();
                if (gender == "M" || gender == "MALE")
                {
                    return "Male";
                }
                else if (gender == "F" || gender == "FEMALE")
                {
                    return "Female";
                }
                else if (gender.Length > 0)
                {
                    return gender;
                }
            }
        }

        return "Not Found";
    }

    private string DetectDocumentType(string text)
    {
        string upperText = text.ToUpper();
        string originalText = text;

        // Aadhar Card detection - Check for 12-digit number pattern first (most reliable)
        bool hasAadharNumberPattern = Regex.IsMatch(text, @"\d{4}\s?\d{4}\s?\d{4}") || Regex.IsMatch(text, @"\d{12}");

        // Aadhar Card detection - Multiple patterns
        if (hasAadharNumberPattern && (
            upperText.Contains("AADHAAR") || upperText.Contains("AADHAR") || 
            upperText.Contains("Aadhaar") || upperText.Contains("Aadhar") ||
            upperText.Contains("GOVERNMENT OF INDIA") || upperText.Contains("GOVERNMENTOFINDIA") ||
            upperText.Contains("UIDAI") || upperText.Contains("UNIQUE IDENTIFICATION") ||
            upperText.Contains("UNIQUEIDENTIFICATION") || upperText.Contains("BHARAT SARKAR") ||
            originalText.Contains("भारत") || originalText.Contains("आधार") || // Hindi text
            originalText.Contains("सरकार") || originalText.Contains("पहचान")))
        {
            return "Aadhar Card";
        }

        // Fallback: If we have a 12-digit number pattern and it looks like an Indian document
        if (hasAadharNumberPattern && (
            upperText.Contains("GOVERNMENT") || upperText.Contains("INDIA") ||
            upperText.Contains("DOB") || upperText.Contains("MALE") || upperText.Contains("FEMALE") ||
            originalText.Contains("जन्म") || originalText.Contains("पुरुष") || originalText.Contains("महिला")))
        {
            return "Aadhar Card";
        }

        // Final fallback: If we have a 12-digit number pattern (most reliable indicator)
        // This handles cases where OCR might not extract text labels properly
        if (hasAadharNumberPattern)
        {
            // Check if it's not a PAN (PAN has letters)
            if (!Regex.IsMatch(text, @"[A-Z]{5}\d{4}[A-Z]", RegexOptions.IgnoreCase))
            {
                return "Aadhar Card";
            }
        }

        // PAN Card detection
        if (upperText.Contains("PERMANENT ACCOUNT NUMBER") || upperText.Contains("PAN") ||
            upperText.Contains("INCOME TAX DEPARTMENT") || upperText.Contains("INCOME TAX DEPTT") ||
            Regex.IsMatch(text, @"[A-Z]{5}\d{4}[A-Z]", RegexOptions.IgnoreCase))
        {
            return "PAN Card";
        }

        // Passport detection
        if (upperText.Contains("PASSPORT") || upperText.Contains("PASSPORT NO"))
        {
            return "Passport";
        }

        // Driver's License detection
        if (upperText.Contains("DRIVER") && upperText.Contains("LICENSE"))
        {
            return "Driver's License";
        }

        // National ID Card detection
        if (upperText.Contains("NATIONAL ID") || upperText.Contains("ID CARD"))
        {
            return "National ID Card";
        }

        // Generic ID detection
        if (upperText.Contains("ID") || upperText.Contains("IDENTITY"))
        {
            return "Identity Document";
        }

        return "Unknown";
    }

    private string GenerateHtmlReport(KycData kycData, string rawText)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("  <head>");
        html.AppendLine("    <meta charset=\"utf-8\" />");
        html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine("    <title>KYC Agent Verification</title>");
        html.AppendLine("    <style>");
        html.AppendLine("      :root {");
        html.AppendLine("        --bg0: #f6f7fb;");
        html.AppendLine("        --bg1: #eef2ff;");
        html.AppendLine("        --card: #ffffff;");
        html.AppendLine("        --muted: #6b7280;");
        html.AppendLine("        --text: #111827;");
        html.AppendLine("        --border: #e5e7eb;");
        html.AppendLine("        --shadow: 0 12px 30px rgba(17, 24, 39, 0.08);");
        html.AppendLine("      }");
        html.AppendLine("      * { box-sizing: border-box; }");
        html.AppendLine("      body {");
        html.AppendLine("        margin: 0;");
        html.AppendLine("        font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial;");
        html.AppendLine("        color: var(--text);");
        html.AppendLine("        background: radial-gradient(1200px 600px at 25% 0%, var(--bg1) 0%, var(--bg0) 55%, #ffffff 100%);");
        html.AppendLine("      }");
        html.AppendLine("      .page {");
        html.AppendLine("        max-width: 920px;");
        html.AppendLine("        margin: 0 auto;");
        html.AppendLine("        padding: 36px 18px 60px;");
        html.AppendLine("      }");
        html.AppendLine("      .title {");
        html.AppendLine("        margin: 0 0 4px;");
        html.AppendLine("        font-size: 22px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        letter-spacing: -0.02em;");
        html.AppendLine("      }");
        html.AppendLine("      .subtitle {");
        html.AppendLine("        margin: 0 0 18px;");
        html.AppendLine("        color: var(--muted);");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("      }");
        html.AppendLine("      .card {");
        html.AppendLine("        background: var(--card);");
        html.AppendLine("        border: 1px solid var(--border);");
        html.AppendLine("        border-radius: 14px;");
        html.AppendLine("        box-shadow: var(--shadow);");
        html.AppendLine("        padding: 16px 16px 14px;");
        html.AppendLine("        margin: 14px 0;");
        html.AppendLine("      }");
        html.AppendLine("      .card h3 {");
        html.AppendLine("        margin: 0 0 10px;");
        html.AppendLine("        font-size: 13px;");
        html.AppendLine("        color: #0f172a;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("      }");
        html.AppendLine("      .table-wrap {");
        html.AppendLine("        border: 1px solid var(--border);");
        html.AppendLine("        border-radius: 12px;");
        html.AppendLine("        overflow: hidden;");
        html.AppendLine("      }");
        html.AppendLine("      table {");
        html.AppendLine("        width: 100%;");
        html.AppendLine("        border-collapse: collapse;");
        html.AppendLine("        font-size: 13px;");
        html.AppendLine("      }");
        html.AppendLine("      thead th {");
        html.AppendLine("        text-align: left;");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        color: var(--muted);");
        html.AppendLine("        background: #fbfdff;");
        html.AppendLine("        padding: 10px 12px;");
        html.AppendLine("        border-bottom: 1px solid var(--border);");
        html.AppendLine("      }");
        html.AppendLine("      tbody td {");
        html.AppendLine("        padding: 11px 12px;");
        html.AppendLine("        border-top: 1px solid var(--border);");
        html.AppendLine("        vertical-align: top;");
        html.AppendLine("      }");
        html.AppendLine("      tbody tr:first-child td {");
        html.AppendLine("        border-top: 0;");
        html.AppendLine("      }");
        html.AppendLine("      tbody tr:hover td {");
        html.AppendLine("        background: #fafafa;");
        html.AppendLine("      }");
        html.AppendLine("      .kp-key {");
        html.AppendLine("        width: 34%;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        color: #0f172a;");
        html.AppendLine("      }");
        html.AppendLine("      .kp-desc {");
        html.AppendLine("        color: #111827;");
        html.AppendLine("      }");
        html.AppendLine("      .pill {");
        html.AppendLine("        display: inline-flex;");
        html.AppendLine("        align-items: center;");
        html.AppendLine("        border-radius: 999px;");
        html.AppendLine("        padding: 6px 10px;");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        border: 1px solid;");
        html.AppendLine("      }");
        html.AppendLine("      .pill-found {");
        html.AppendLine("        background: #dcfce7;");
        html.AppendLine("        border-color: #86efac;");
        html.AppendLine("        color: #166534;");
        html.AppendLine("      }");
        html.AppendLine("      .pill-not-found {");
        html.AppendLine("        background: #fee2e2;");
        html.AppendLine("        border-color: #fca5a5;");
        html.AppendLine("        color: #991b1b;");
        html.AppendLine("      }");
        html.AppendLine("    </style>");
        html.AppendLine("  </head>");
        html.AppendLine("  <body>");
        html.AppendLine("    <main class=\"page\">");
        html.AppendLine("      <h2 class=\"title\">KYC Agent Verification</h2>");
        html.AppendLine("      <p class=\"subtitle\">Extracted from document using OCR</p>");

        // Document Type Card
        html.AppendLine("      <section class=\"card\">");
        html.AppendLine("        <h3>Document Information</h3>");
        html.AppendLine("        <div class=\"table-wrap\">");
        html.AppendLine("          <table>");
        html.AppendLine("            <thead>");
        html.AppendLine("              <tr>");
        html.AppendLine("                <th>Field</th>");
        html.AppendLine("                <th>Value</th>");
        html.AppendLine("              </tr>");
        html.AppendLine("            </thead>");
        html.AppendLine("            <tbody>");
        
        html.AppendLine($"              <tr><td class='kp-key'>Document Type</td><td class='kp-desc'>{EscapeHtml(kycData.DocumentType)}</td></tr>");
        html.AppendLine($"              <tr><td class='kp-key'>Full Name</td><td class='kp-desc'>{EscapeHtml(kycData.FullName)}</td></tr>");
        
        // Display appropriate ID field label based on document type
        string idLabel = kycData.DocumentType == "Aadhar Card" ? "Aadhar Number" :
                        kycData.DocumentType == "PAN Card" ? "PAN Number" :
                        "ID/Passport Number";
        html.AppendLine($"              <tr><td class='kp-key'>{idLabel}</td><td class='kp-desc'>{EscapeHtml(kycData.IdNumber)}</td></tr>");
        
        html.AppendLine($"              <tr><td class='kp-key'>Date of Birth</td><td class='kp-desc'>{EscapeHtml(kycData.DateOfBirth)}</td></tr>");
        
        // Show Father's Name for PAN cards
        if (kycData.DocumentType == "PAN Card" && kycData.FatherName != "Not Found")
        {
            html.AppendLine($"              <tr><td class='kp-key'>Father's Name</td><td class='kp-desc'>{EscapeHtml(kycData.FatherName)}</td></tr>");
        }
        
        // Show Gender for Aadhar cards
        if (kycData.DocumentType == "Aadhar Card" && kycData.Gender != "Not Found")
        {
            html.AppendLine($"              <tr><td class='kp-key'>Gender</td><td class='kp-desc'>{EscapeHtml(kycData.Gender)}</td></tr>");
        }
        
        html.AppendLine($"              <tr><td class='kp-key'>Address</td><td class='kp-desc'>{EscapeHtml(kycData.Address)}</td></tr>");
        
        // Nationality and Expiry Date (not always present for Indian documents)
        if (kycData.Nationality != "Not Found")
        {
            html.AppendLine($"              <tr><td class='kp-key'>Nationality</td><td class='kp-desc'>{EscapeHtml(kycData.Nationality)}</td></tr>");
        }
        if (kycData.ExpiryDate != "Not Found")
        {
            html.AppendLine($"              <tr><td class='kp-key'>Expiry Date</td><td class='kp-desc'>{EscapeHtml(kycData.ExpiryDate)}</td></tr>");
        }
        
        html.AppendLine("            </tbody>");
        html.AppendLine("          </table>");
        html.AppendLine("        </div>");

        // Status pills
        html.AppendLine("        <div style=\"margin-top: 12px;\">");
        int foundCount = 0;
        int totalFields = 6; // Base fields
        if (kycData.FullName != "Not Found") foundCount++;
        if (kycData.IdNumber != "Not Found") foundCount++;
        if (kycData.DateOfBirth != "Not Found") foundCount++;
        if (kycData.Address != "Not Found") foundCount++;
        
        // Count additional fields based on document type
        if (kycData.DocumentType == "PAN Card" && kycData.FatherName != "Not Found")
        {
            foundCount++;
            totalFields++;
        }
        if (kycData.DocumentType == "Aadhar Card" && kycData.Gender != "Not Found")
        {
            foundCount++;
            totalFields++;
        }
        
        string statusClass = foundCount >= 3 ? "pill-found" : "pill-not-found";
        string statusText = foundCount >= 3 ? $"Verification: {foundCount}/{totalFields} fields found" : $"Verification: {foundCount}/{totalFields} fields found (Incomplete)";
        
        html.AppendLine($"          <span class=\"pill {statusClass}\">{EscapeHtml(statusText)}</span>");
        html.AppendLine("        </div>");
        html.AppendLine("      </section>");
        html.AppendLine("    </main>");
        html.AppendLine("  </body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private string GenerateMultipleDocumentsHtmlReport(List<(KycData data, int index)> allKycData, string? faceMatchHtml)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("  <head>");
        html.AppendLine("    <meta charset=\"utf-8\" />");
        html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine("    <title>KYC Agent Verification - Multiple Documents</title>");
        html.AppendLine("    <style>");
        html.AppendLine("      :root {");
        html.AppendLine("        --bg0: #f6f7fb;");
        html.AppendLine("        --bg1: #eef2ff;");
        html.AppendLine("        --card: #ffffff;");
        html.AppendLine("        --muted: #6b7280;");
        html.AppendLine("        --text: #111827;");
        html.AppendLine("        --border: #e5e7eb;");
        html.AppendLine("        --shadow: 0 12px 30px rgba(17, 24, 39, 0.08);");
        html.AppendLine("      }");
        html.AppendLine("      * { box-sizing: border-box; }");
        html.AppendLine("      body {");
        html.AppendLine("        margin: 0;");
        html.AppendLine("        font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial;");
        html.AppendLine("        color: var(--text);");
        html.AppendLine("        background: radial-gradient(1200px 600px at 25% 0%, var(--bg1) 0%, var(--bg0) 55%, #ffffff 100%);");
        html.AppendLine("      }");
        html.AppendLine("      .page {");
        html.AppendLine("        max-width: 920px;");
        html.AppendLine("        margin: 0 auto;");
        html.AppendLine("        padding: 36px 18px 60px;");
        html.AppendLine("      }");
        html.AppendLine("      .title {");
        html.AppendLine("        margin: 0 0 4px;");
        html.AppendLine("        font-size: 22px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        letter-spacing: -0.02em;");
        html.AppendLine("      }");
        html.AppendLine("      .subtitle {");
        html.AppendLine("        margin: 0 0 18px;");
        html.AppendLine("        color: var(--muted);");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("      }");
        html.AppendLine("      .card {");
        html.AppendLine("        background: var(--card);");
        html.AppendLine("        border: 1px solid var(--border);");
        html.AppendLine("        border-radius: 14px;");
        html.AppendLine("        box-shadow: var(--shadow);");
        html.AppendLine("        padding: 16px 16px 14px;");
        html.AppendLine("        margin: 14px 0;");
        html.AppendLine("      }");
        html.AppendLine("      .card h3 {");
        html.AppendLine("        margin: 0 0 10px;");
        html.AppendLine("        font-size: 13px;");
        html.AppendLine("        color: #0f172a;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("      }");
        html.AppendLine("      .table-wrap {");
        html.AppendLine("        border: 1px solid var(--border);");
        html.AppendLine("        border-radius: 12px;");
        html.AppendLine("        overflow: hidden;");
        html.AppendLine("      }");
        html.AppendLine("      table {");
        html.AppendLine("        width: 100%;");
        html.AppendLine("        border-collapse: collapse;");
        html.AppendLine("        font-size: 13px;");
        html.AppendLine("      }");
        html.AppendLine("      thead th {");
        html.AppendLine("        text-align: left;");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        color: var(--muted);");
        html.AppendLine("        background: #fbfdff;");
        html.AppendLine("        padding: 10px 12px;");
        html.AppendLine("        border-bottom: 1px solid var(--border);");
        html.AppendLine("      }");
        html.AppendLine("      tbody td {");
        html.AppendLine("        padding: 11px 12px;");
        html.AppendLine("        border-top: 1px solid var(--border);");
        html.AppendLine("        vertical-align: top;");
        html.AppendLine("      }");
        html.AppendLine("      tbody tr:first-child td {");
        html.AppendLine("        border-top: 0;");
        html.AppendLine("      }");
        html.AppendLine("      tbody tr:hover td {");
        html.AppendLine("        background: #fafafa;");
        html.AppendLine("      }");
        html.AppendLine("      .kp-key {");
        html.AppendLine("        width: 34%;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        color: #0f172a;");
        html.AppendLine("      }");
        html.AppendLine("      .kp-desc {");
        html.AppendLine("        color: #111827;");
        html.AppendLine("      }");
        html.AppendLine("      .pill {");
        html.AppendLine("        display: inline-flex;");
        html.AppendLine("        align-items: center;");
        html.AppendLine("        border-radius: 999px;");
        html.AppendLine("        padding: 6px 10px;");
        html.AppendLine("        font-size: 12px;");
        html.AppendLine("        font-weight: 700;");
        html.AppendLine("        border: 1px solid;");
        html.AppendLine("      }");
        html.AppendLine("      .pill-found {");
        html.AppendLine("        background: #dcfce7;");
        html.AppendLine("        border-color: #86efac;");
        html.AppendLine("        color: #166534;");
        html.AppendLine("      }");
        html.AppendLine("      .pill-not-found {");
        html.AppendLine("        background: #fee2e2;");
        html.AppendLine("        border-color: #fca5a5;");
        html.AppendLine("        color: #991b1b;");
        html.AppendLine("      }");
        html.AppendLine("    </style>");
        html.AppendLine("  </head>");
        html.AppendLine("  <body>");
        html.AppendLine("    <main class=\"page\">");
        html.AppendLine("      <h2 class=\"title\">KYC Agent Verification</h2>");
        html.AppendLine("      <p class=\"subtitle\">Extracted from " + allKycData.Count + " document(s) using OCR</p>");

        // Face match section
        if (!string.IsNullOrEmpty(faceMatchHtml))
        {
            html.AppendLine("      <section class=\"card\">");
            html.AppendLine("        <h3>Face Verification</h3>");
            html.AppendLine(faceMatchHtml);
            html.AppendLine("      </section>");
        }

        // Process each document
        foreach (var (kycData, index) in allKycData)
        {
            html.AppendLine("      <section class=\"card\">");
            html.AppendLine("        <h3>Document " + index + " Information</h3>");
            html.AppendLine("        <div class=\"table-wrap\">");
            html.AppendLine("          <table>");
            html.AppendLine("            <thead>");
            html.AppendLine("              <tr>");
            html.AppendLine("                <th>Field</th>");
            html.AppendLine("                <th>Value</th>");
            html.AppendLine("              </tr>");
            html.AppendLine("            </thead>");
            html.AppendLine("            <tbody>");
            
            html.AppendLine($"              <tr><td class='kp-key'>Document Type</td><td class='kp-desc'>{EscapeHtml(kycData.DocumentType)}</td></tr>");
            html.AppendLine($"              <tr><td class='kp-key'>Full Name</td><td class='kp-desc'>{EscapeHtml(kycData.FullName)}</td></tr>");
            
            string idLabel = kycData.DocumentType == "Aadhar Card" ? "Aadhar Number" :
                            kycData.DocumentType == "PAN Card" ? "PAN Number" :
                            "ID/Passport Number";
            html.AppendLine($"              <tr><td class='kp-key'>{idLabel}</td><td class='kp-desc'>{EscapeHtml(kycData.IdNumber)}</td></tr>");
            html.AppendLine($"              <tr><td class='kp-key'>Date of Birth</td><td class='kp-desc'>{EscapeHtml(kycData.DateOfBirth)}</td></tr>");
            
            if (kycData.DocumentType == "PAN Card" && kycData.FatherName != "Not Found")
            {
                html.AppendLine($"              <tr><td class='kp-key'>Father's Name</td><td class='kp-desc'>{EscapeHtml(kycData.FatherName)}</td></tr>");
            }
            
            if (kycData.DocumentType == "Aadhar Card" && kycData.Gender != "Not Found")
            {
                html.AppendLine($"              <tr><td class='kp-key'>Gender</td><td class='kp-desc'>{EscapeHtml(kycData.Gender)}</td></tr>");
            }
            
            html.AppendLine($"              <tr><td class='kp-key'>Address</td><td class='kp-desc'>{EscapeHtml(kycData.Address)}</td></tr>");
            
            if (kycData.Nationality != "Not Found")
            {
                html.AppendLine($"              <tr><td class='kp-key'>Nationality</td><td class='kp-desc'>{EscapeHtml(kycData.Nationality)}</td></tr>");
            }
            if (kycData.ExpiryDate != "Not Found")
            {
                html.AppendLine($"              <tr><td class='kp-key'>Expiry Date</td><td class='kp-desc'>{EscapeHtml(kycData.ExpiryDate)}</td></tr>");
            }
            
            html.AppendLine("            </tbody>");
            html.AppendLine("          </table>");
            html.AppendLine("        </div>");

            // Status pills
            html.AppendLine("        <div style=\"margin-top: 12px;\">");
            int foundCount = 0;
            int totalFields = 6;
            if (kycData.FullName != "Not Found") foundCount++;
            if (kycData.IdNumber != "Not Found") foundCount++;
            if (kycData.DateOfBirth != "Not Found") foundCount++;
            if (kycData.Address != "Not Found") foundCount++;
            
            if (kycData.DocumentType == "PAN Card" && kycData.FatherName != "Not Found")
            {
                foundCount++;
                totalFields++;
            }
            if (kycData.DocumentType == "Aadhar Card" && kycData.Gender != "Not Found")
            {
                foundCount++;
                totalFields++;
            }
            
            string statusClass = foundCount >= 3 ? "pill-found" : "pill-not-found";
            string statusText = foundCount >= 3 ? $"Verification: {foundCount}/{totalFields} fields found" : $"Verification: {foundCount}/{totalFields} fields found (Incomplete)";
            
            html.AppendLine($"          <span class=\"pill {statusClass}\">{EscapeHtml(statusText)}</span>");
            html.AppendLine("        </div>");
            html.AppendLine("      </section>");
        }

        html.AppendLine("    </main>");
        html.AppendLine("  </body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private string GenerateFaceMatchHtml(bool match, int matchScore, string message)
    {
        var html = new StringBuilder();
        string color = match ? "#166534" : "#991b1b";
        string bgColor = match ? "#dcfce7" : "#fee2e2";
        string borderColor = match ? "#86efac" : "#fca5a5";
        string icon = match ? "✅" : "❌";
        
        html.AppendLine($"<div style='color: {color}; padding: 12px; background: {bgColor}; border: 1px solid {borderColor}; border-radius: 8px;'>");
        html.AppendLine($"  <div style='font-weight: 700; margin-bottom: 4px;'>{icon} {EscapeHtml(message)}</div>");
        html.AppendLine($"  <div style='font-size: 12px;'>Match Score: {matchScore}/5</div>");
        html.AppendLine("</div>");
        
        return html.ToString();
    }

    private string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    private class KycData
    {
        public string FullName { get; set; } = "Not Found";
        public string IdNumber { get; set; } = "Not Found";
        public string DateOfBirth { get; set; } = "Not Found";
        public string Address { get; set; } = "Not Found";
        public string Nationality { get; set; } = "Not Found";
        public string ExpiryDate { get; set; } = "Not Found";
        public string DocumentType { get; set; } = "Unknown";
        public string FatherName { get; set; } = "Not Found"; // For PAN cards
        public string Gender { get; set; } = "Not Found"; // For Aadhar cards
    }
}
