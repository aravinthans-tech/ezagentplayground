# KYC Verification API - Code Examples

## Overview

The KYC Verification API provides two endpoints:

1. **`/api/KycAgent/process`** - Extract KYC data from documents (simple extraction)
2. **`/api/KycAgent/verify`** - Comprehensive verification with address matching and consistency checks

### `/api/KycAgent/process` Endpoint

Extracts KYC fields from one or more documents using OCR. Optionally performs face matching if license and selfie images are provided.

### `/api/KycAgent/verify` Endpoint

Performs comprehensive document verification including:
- Document text extraction using OCR
- Address extraction and verification with Google Maps
- Address consistency checking between multiple documents
- Face matching between license and selfie images (optional)
- Name consistency checking

---

## Endpoint 1: Process Documents

**POST** `/api/KycAgent/process`

### Authentication

Include the API key in the request header:
```
X-API-Key: your-api-key-here
```

### Request Format

**Content-Type:** `multipart/form-data`

#### Required Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `documents` | File[] | One or more document files (PDF, images, etc.) |

#### Optional Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `licenseImage` | File | License/ID image for face matching |
| `selfieImage` | File | Selfie image for face matching |

### Response Structure

```json
{
  "id": 1,
  "output": "<html>...</html>",
  "encryptOutput": null
}
```

The `output` field contains an HTML report with extracted KYC data from all documents. If face matching is performed, it will be included in the HTML report.

---

## Endpoint 2: Verify Documents

**POST** `/api/KycAgent/verify`

### Authentication

Include the API key in the request header:
```
X-API-Key: your-api-key-here
```

### Request Format

**Content-Type:** `multipart/form-data`

#### Required Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `documents` | File[] | At least 2 document files (PDF, images, etc.) |
| `expectedAddress` | string | The expected address to match against extracted addresses |

#### Optional Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `modelChoice` | string | "Mistral" | LLM model: "Mistral" or "OpenAI" |
| `consistencyThreshold` | double | 0.82 | Address consistency threshold (0.0 to 1.0) |
| `licenseImage` | File | null | License/ID image for face matching |
| `selfieImage` | File | null | Selfie image for face matching |

### Response Structure

```json
{
  "finalResult": true,
  "addressConsistencyScore": 0.95,
  "nameConsistencyScore": 0.98,
  "documentConsistencyScore": 0.95,
  "averageAuthenticityScore": 0.92,
  "documentsConsistent": true,
  "documents": [
    {
      "documentIndex": 1,
      "extractedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
      "extractedName": "ARAVINDHAN S",
      "similarityToExpected": 1.0,
      "addressMatch": true,
      "googleMapsVerified": true,
      "googleMapsFormattedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004, India",
      "authenticityScore": 0.95,
      "extractedFields": {
        "full_name": "ARAVINDHAN S",
        "address": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
        "document_type": "Driving License",
        ...
      }
    }
  ],
  "faceMatch": {
    "match": true,
    "matchScore": 4,
    "message": "✅ Photo Verification Passed<br>Match Score: 4/5",
    "licenseFaceImage": "base64_encoded_image",
    "selfieFaceImage": "base64_encoded_image"
  },
  "statusHtml": "<div>...</div>",
  "verificationTableHtml": "<div>...</div>",
  "extractedFields": {
    "document_1": {...},
    "document_2": {...}
  }
}
```

## Code Examples

### Endpoint 1: Process Documents (`/api/KycAgent/process`)

This endpoint extracts KYC data from one or more documents. It's simpler than the verify endpoint and doesn't require an expected address.

#### cURL

```bash
curl -X POST "https://localhost:51347/api/KycAgent/process" \
  -H "X-API-Key: your-api-key-here" \
  -F "documents=@/path/to/document1.pdf" \
  -F "documents=@/path/to/document2.pdf" \
  -F "licenseImage=@/path/to/license.jpg" \
  -F "selfieImage=@/path/to/selfie.jpg"
```

#### Python

```python
import requests

url = "https://localhost:51347/api/KycAgent/process"
headers = {
    "X-API-Key": "your-api-key-here"
}

# Prepare files
files = [
    ("documents", ("document1.pdf", open("document1.pdf", "rb"), "application/pdf")),
    ("documents", ("document2.pdf", open("document2.pdf", "rb"), "application/pdf")),
    ("licenseImage", ("license.jpg", open("license.jpg", "rb"), "image/jpeg")),
    ("selfieImage", ("selfie.jpg", open("selfie.jpg", "rb"), "image/jpeg"))
]

# Make request
response = requests.post(url, headers=headers, files=files)

if response.status_code == 200:
    result = response.json()
    
    if result["id"] == 1:
        # result["output"] contains the HTML report with extracted KYC data
        html_content = result["output"]
        print("KYC Agent HTML:")
        print(html_content)
    else:
        print(f"Error: {result['EncryptOutput']}")
else:
    print(f"Error: {response.status_code}")
    print(response.text)
```

#### JavaScript (Browser)

```javascript
async function processKYC() {
    const formData = new FormData();
    
    // Add documents (at least 1 required)
    const doc1Input = document.getElementById('document1');
    const doc2Input = document.getElementById('document2');
    formData.append('documents', doc1Input.files[0]);
    if (doc2Input.files[0]) {
        formData.append('documents', doc2Input.files[0]);
    }
    
    // Add face images if provided
    const licenseInput = document.getElementById('licenseImage');
    const selfieInput = document.getElementById('selfieImage');
    if (licenseInput.files[0]) {
        formData.append('licenseImage', licenseInput.files[0]);
    }
    if (selfieInput.files[0]) {
        formData.append('selfieImage', selfieInput.files[0]);
    }
    
    try {
        const response = await fetch('https://localhost:51347/api/KycAgent/process', {
            method: 'POST',
            headers: {
                'X-API-Key': 'your-api-key-here'
            },
            body: formData
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const result = await response.json();
        
        if (result.id === 1) {
            // Display HTML report
            document.getElementById('results').innerHTML = result.output;
        } else {
            console.error('Error:', result.EncryptOutput);
        }
        
        return result;
    } catch (error) {
        console.error('Error:', error);
        throw error;
    }
}
```

#### JavaScript (Node.js)

```javascript
const FormData = require('form-data');
const fs = require('fs');
const axios = require('axios');

async function processKYC() {
    const formData = new FormData();
    
    // Add documents
    formData.append('documents', fs.createReadStream('document1.pdf'));
    formData.append('documents', fs.createReadStream('document2.pdf'));
    
    // Add face images if available
    if (fs.existsSync('license.jpg')) {
        formData.append('licenseImage', fs.createReadStream('license.jpg'));
    }
    if (fs.existsSync('selfie.jpg')) {
        formData.append('selfieImage', fs.createReadStream('selfie.jpg'));
    }
    
    try {
        const response = await axios.post(
            'https://localhost:51347/api/KycAgent/process',
            formData,
            {
                headers: {
                    'X-API-Key': 'your-api-key-here',
                    ...formData.getHeaders()
                }
            }
        );
        
        const result = response.data;
        
        if (result.id === 1) {
            console.log('HTML Report:', result.output);
        } else {
            console.error('Error:', result.EncryptOutput);
        }
        
        return result;
    } catch (error) {
        console.error('Error:', error.response?.data || error.message);
        throw error;
    }
}

processKYC();
```

#### C#

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

class KycProcessExample
{
    private static readonly HttpClient client = new HttpClient();
    private const string API_BASE_URL = "https://localhost:51347";
    private const string API_KEY = "your-api-key-here";

    static async Task Main(string[] args)
    {
        client.DefaultRequestHeaders.Add("X-API-Key", API_KEY);

        using var formData = new MultipartFormDataContent();

        // Add documents
        var doc1Content = new ByteArrayContent(File.ReadAllBytes("document1.pdf"));
        doc1Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        formData.Add(doc1Content, "documents", "document1.pdf");

        var doc2Content = new ByteArrayContent(File.ReadAllBytes("document2.pdf"));
        doc2Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        formData.Add(doc2Content, "documents", "document2.pdf");

        // Add face images if available
        if (File.Exists("license.jpg"))
        {
            var licenseContent = new ByteArrayContent(File.ReadAllBytes("license.jpg"));
            licenseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formData.Add(licenseContent, "licenseImage", "license.jpg");
        }

        if (File.Exists("selfie.jpg"))
        {
            var selfieContent = new ByteArrayContent(File.ReadAllBytes("selfie.jpg"));
            selfieContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formData.Add(selfieContent, "selfieImage", "selfie.jpg");
        }

        try
        {
            var response = await client.PostAsync($"{API_BASE_URL}/api/KycAgent/process", formData);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<KycProcessResult>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result.Id == 1)
                {
                    Console.WriteLine("HTML Report:");
                    Console.WriteLine(result.Output);
                }
                else
                {
                    Console.WriteLine($"Error: {result.EncryptOutput}");
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(responseContent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

public class KycProcessResult
{
    public int Id { get; set; }
    public string? Output { get; set; }
    public string? EncryptOutput { get; set; }
}
```

---

### Endpoint 2: Verify Documents (`/api/KycAgent/verify`)

#### cURL

```bash
curl -X POST "http://localhost:5000/api/KycAgent/verify" \
  -H "X-API-Key: your-api-key-here" \
  -F "documents=@/path/to/document1.pdf" \
  -F "documents=@/path/to/document2.pdf" \
  -F "expectedAddress=10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004" \
  -F "modelChoice=Mistral" \
  -F "consistencyThreshold=0.82" \
  -F "licenseImage=@/path/to/license.jpg" \
  -F "selfieImage=@/path/to/selfie.jpg"
```

### Python

```python
import requests

url = "http://localhost:5000/api/KycAgent/verify"
headers = {
    "X-API-Key": "your-api-key-here"
}

# Prepare files
files = [
    ("documents", ("document1.pdf", open("document1.pdf", "rb"), "application/pdf")),
    ("documents", ("document2.pdf", open("document2.pdf", "rb"), "application/pdf")),
    ("licenseImage", ("license.jpg", open("license.jpg", "rb"), "image/jpeg")),
    ("selfieImage", ("selfie.jpg", open("selfie.jpg", "rb"), "image/jpeg"))
]

# Prepare form data
data = {
    "expectedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
    "modelChoice": "Mistral",
    "consistencyThreshold": 0.82
}

# Make request
response = requests.post(url, headers=headers, files=files, data=data)

if response.status_code == 200:
    result = response.json()
    
    print(f"Verification Result: {'✅ Passed' if result['finalResult'] else '❌ Failed'}")
    print(f"Address Consistency: {result['addressConsistencyScore']:.2%}")
    print(f"Name Consistency: {result['nameConsistencyScore']:.2%}")
    
    # Check face match if provided
    if result.get('faceMatch'):
        face_match = result['faceMatch']
        print(f"Face Match: {'✅ Passed' if face_match['match'] else '❌ Failed'}")
        print(f"Face Match Score: {face_match['matchScore']}/5")
        print(f"Message: {face_match['message']}")
    
    # Display document details
    for doc in result['documents']:
        print(f"\nDocument {doc['documentIndex']}:")
        print(f"  Name: {doc['extractedName']}")
        print(f"  Address: {doc['extractedAddress']}")
        print(f"  Address Match: {'✅ Yes' if doc['addressMatch'] else '❌ No'}")
        print(f"  Google Maps Verified: {'✅ Yes' if doc['googleMapsVerified'] else '❌ No'}")
        print(f"  Similarity: {doc['similarityToExpected']:.2%}")
else:
    print(f"Error: {response.status_code}")
    print(response.text)
```

### JavaScript (Browser)

```javascript
async function verifyKYC() {
    const formData = new FormData();
    
    // Add documents (at least 2 required)
    const doc1Input = document.getElementById('document1');
    const doc2Input = document.getElementById('document2');
    formData.append('documents', doc1Input.files[0]);
    formData.append('documents', doc2Input.files[0]);
    
    // Add expected address
    formData.append('expectedAddress', '10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004');
    
    // Optional parameters
    formData.append('modelChoice', 'Mistral');
    formData.append('consistencyThreshold', '0.82');
    
    // Add face images if provided
    const licenseInput = document.getElementById('licenseImage');
    const selfieInput = document.getElementById('selfieImage');
    if (licenseInput.files[0]) {
        formData.append('licenseImage', licenseInput.files[0]);
    }
    if (selfieInput.files[0]) {
        formData.append('selfieImage', selfieInput.files[0]);
    }
    
    try {
        const response = await fetch('http://localhost:5000/api/KycAgent/verify', {
            method: 'POST',
            headers: {
                'X-API-Key': 'your-api-key-here'
            },
            body: formData
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const result = await response.json();
        
        // Display results
        console.log('Verification Result:', result.finalResult ? '✅ Passed' : '❌ Failed');
        console.log('Address Consistency:', `${(result.addressConsistencyScore * 100).toFixed(2)}%`);
        console.log('Name Consistency:', `${(result.nameConsistencyScore * 100).toFixed(2)}%`);
        
        // Display face match results
        if (result.faceMatch) {
            console.log('Face Match:', result.faceMatch.match ? '✅ Passed' : '❌ Failed');
            console.log('Face Match Score:', `${result.faceMatch.matchScore}/5`);
            console.log('Message:', result.faceMatch.message);
            
            // Display face images if available
            if (result.faceMatch.licenseFaceImage) {
                const licenseImg = document.createElement('img');
                licenseImg.src = `data:image/png;base64,${result.faceMatch.licenseFaceImage}`;
                document.body.appendChild(licenseImg);
            }
        }
        
        // Display verification table HTML
        if (result.verificationTableHtml) {
            document.getElementById('results').innerHTML = result.verificationTableHtml;
        }
        
        return result;
    } catch (error) {
        console.error('Error:', error);
        throw error;
    }
}
```

### JavaScript (Node.js)

```javascript
const FormData = require('form-data');
const fs = require('fs');
const axios = require('axios');

async function verifyKYC() {
    const formData = new FormData();
    
    // Add documents
    formData.append('documents', fs.createReadStream('document1.pdf'));
    formData.append('documents', fs.createReadStream('document2.pdf'));
    
    // Add expected address
    formData.append('expectedAddress', '10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004');
    
    // Optional parameters
    formData.append('modelChoice', 'Mistral');
    formData.append('consistencyThreshold', '0.82');
    
    // Add face images if available
    if (fs.existsSync('license.jpg')) {
        formData.append('licenseImage', fs.createReadStream('license.jpg'));
    }
    if (fs.existsSync('selfie.jpg')) {
        formData.append('selfieImage', fs.createReadStream('selfie.jpg'));
    }
    
    try {
        const response = await axios.post(
            'http://localhost:5000/api/KycAgent/verify',
            formData,
            {
                headers: {
                    'X-API-Key': 'your-api-key-here',
                    ...formData.getHeaders()
                }
            }
        );
        
        const result = response.data;
        
        console.log('Verification Result:', result.finalResult ? '✅ Passed' : '❌ Failed');
        console.log('Address Consistency:', `${(result.addressConsistencyScore * 100).toFixed(2)}%`);
        console.log('Name Consistency:', `${(result.nameConsistencyScore * 100).toFixed(2)}%`);
        
        if (result.faceMatch) {
            console.log('Face Match:', result.faceMatch.match ? '✅ Passed' : '❌ Failed');
            console.log('Face Match Score:', `${result.faceMatch.matchScore}/5`);
        }
        
        return result;
    } catch (error) {
        console.error('Error:', error.response?.data || error.message);
        throw error;
    }
}

verifyKYC();
```

### C#

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

class KycVerificationExample
{
    private static readonly HttpClient client = new HttpClient();
    private const string API_BASE_URL = "http://localhost:5000";
    private const string API_KEY = "your-api-key-here";

    static async Task Main(string[] args)
    {
        client.DefaultRequestHeaders.Add("X-API-Key", API_KEY);

        using var formData = new MultipartFormDataContent();

        // Add documents (at least 2 required)
        var doc1Content = new ByteArrayContent(File.ReadAllBytes("document1.pdf"));
        doc1Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        formData.Add(doc1Content, "documents", "document1.pdf");

        var doc2Content = new ByteArrayContent(File.ReadAllBytes("document2.pdf"));
        doc2Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        formData.Add(doc2Content, "documents", "document2.pdf");

        // Add expected address
        formData.Add(new StringContent("10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004"), "expectedAddress");

        // Optional parameters
        formData.Add(new StringContent("Mistral"), "modelChoice");
        formData.Add(new StringContent("0.82"), "consistencyThreshold");

        // Add face images if available
        if (File.Exists("license.jpg"))
        {
            var licenseContent = new ByteArrayContent(File.ReadAllBytes("license.jpg"));
            licenseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formData.Add(licenseContent, "licenseImage", "license.jpg");
        }

        if (File.Exists("selfie.jpg"))
        {
            var selfieContent = new ByteArrayContent(File.ReadAllBytes("selfie.jpg"));
            selfieContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formData.Add(selfieContent, "selfieImage", "selfie.jpg");
        }

        try
        {
            var response = await client.PostAsync($"{API_BASE_URL}/api/KycAgent/verify", formData);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<KycVerificationResult>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                Console.WriteLine($"Verification Result: {(result.FinalResult ? "✅ Passed" : "❌ Failed")}");
                Console.WriteLine($"Address Consistency: {result.AddressConsistencyScore:P2}");
                Console.WriteLine($"Name Consistency: {result.NameConsistencyScore:P2}");

                if (result.FaceMatch != null)
                {
                    Console.WriteLine($"Face Match: {(result.FaceMatch.Match ? "✅ Passed" : "❌ Failed")}");
                    Console.WriteLine($"Face Match Score: {result.FaceMatch.MatchScore}/5");
                    Console.WriteLine($"Message: {result.FaceMatch.Message}");
                }

                foreach (var doc in result.Documents)
                {
                    Console.WriteLine($"\nDocument {doc.DocumentIndex}:");
                    Console.WriteLine($"  Name: {doc.ExtractedName}");
                    Console.WriteLine($"  Address: {doc.ExtractedAddress}");
                    Console.WriteLine($"  Address Match: {(doc.AddressMatch ? "✅ Yes" : "❌ No")}");
                    Console.WriteLine($"  Google Maps Verified: {(doc.GoogleMapsVerified ? "✅ Yes" : "❌ No")}");
                    Console.WriteLine($"  Similarity: {doc.SimilarityToExpected:P2}");
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine(responseContent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

// Response model classes
public class KycVerificationResult
{
    public bool FinalResult { get; set; }
    public double AddressConsistencyScore { get; set; }
    public double NameConsistencyScore { get; set; }
    public double DocumentConsistencyScore { get; set; }
    public double AverageAuthenticityScore { get; set; }
    public bool DocumentsConsistent { get; set; }
    public List<DocumentVerification> Documents { get; set; } = new();
    public FaceMatchResult? FaceMatch { get; set; }
    public string StatusHtml { get; set; } = string.Empty;
    public string VerificationTableHtml { get; set; } = string.Empty;
}

public class DocumentVerification
{
    public int DocumentIndex { get; set; }
    public string ExtractedAddress { get; set; } = string.Empty;
    public string ExtractedName { get; set; } = string.Empty;
    public double SimilarityToExpected { get; set; }
    public bool AddressMatch { get; set; }
    public bool GoogleMapsVerified { get; set; }
    public string GoogleMapsFormattedAddress { get; set; } = string.Empty;
    public double AuthenticityScore { get; set; }
}

public class FaceMatchResult
{
    public bool Match { get; set; }
    public int MatchScore { get; set; }
    public string Message { get; set; } = string.Empty;
    public byte[]? LicenseFaceImage { get; set; }
    public byte[]? SelfieFaceImage { get; set; }
}
```

## Response Examples

### Success Response (200 OK)

```json
{
  "finalResult": true,
  "addressConsistencyScore": 0.95,
  "nameConsistencyScore": 0.98,
  "documentConsistencyScore": 0.95,
  "averageAuthenticityScore": 0.92,
  "documentsConsistent": true,
  "documents": [
    {
      "documentIndex": 1,
      "extractedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
      "extractedName": "ARAVINDHAN S",
      "similarityToExpected": 1.0,
      "addressMatch": true,
      "googleMapsVerified": true,
      "googleMapsFormattedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004, India",
      "authenticityScore": 0.95
    },
    {
      "documentIndex": 2,
      "extractedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
      "extractedName": "Aravinthan",
      "similarityToExpected": 0.95,
      "addressMatch": true,
      "googleMapsVerified": true,
      "googleMapsFormattedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004, India",
      "authenticityScore": 0.89
    }
  ],
  "faceMatch": {
    "match": true,
    "matchScore": 4,
    "message": "✅ Photo Verification Passed<br>Match Score: 4/5",
    "licenseFaceImage": "iVBORw0KGgoAAAANSUhEUgAA...",
    "selfieFaceImage": "iVBORw0KGgoAAAANSUhEUgAA..."
  },
  "statusHtml": "<div>...</div>",
  "verificationTableHtml": "<div>...</div>"
}
```

### Error Response (400 Bad Request)

```json
{
  "statusHtml": "❌ <b style='color:red;'>Please upload at least two documents.</b>",
  "finalResult": false,
  "addressConsistencyScore": 0.0,
  "nameConsistencyScore": 0.0,
  "documentConsistencyScore": 0.0,
  "averageAuthenticityScore": 0.0,
  "documentsConsistent": false,
  "documents": []
}
```

## Face Matching

### Requirements

- Face matching requires both `licenseImage` and `selfieImage`
- Face match score must be >= 4 (out of 5) to pass
- Threshold is configurable in `appsettings.json` (`KycVerification:FaceMatchThreshold`)

### Face Match Result

```json
{
  "match": true,
  "matchScore": 4,
  "message": "✅ Photo Verification Passed<br>Match Score: 4/5",
  "licenseFaceImage": "base64_encoded_png_image",
  "selfieFaceImage": "base64_encoded_png_image"
}
```

## Configuration

### Face Match Threshold

Configure in `appsettings.json`:

```json
{
  "KycVerification": {
    "FaceMatchThreshold": 4,
    "ConsistencyThreshold": 0.82,
    "DefaultModel": "Mistral"
  }
}
```

- `FaceMatchThreshold`: Minimum match score (0-5) required to pass face verification. Default: 4
- `ConsistencyThreshold`: Minimum similarity (0.0-1.0) required for address matching. Default: 0.82

## Best Practices

1. **Document Quality**: Ensure documents are clear and readable for better OCR accuracy
2. **Address Format**: Provide the expected address in the same format as it appears on documents
3. **Face Images**: Use clear, front-facing photos for better face matching accuracy
4. **Error Handling**: Always check response status codes and handle errors appropriately
5. **API Key Security**: Never expose API keys in client-side code. Use server-side proxy for web applications

## Common Issues

### "No face detected" Error

- Ensure images contain clear, front-facing faces
- Check image quality and lighting
- Face should be clearly visible and not obscured

### Low Address Consistency

- Verify addresses are extracted correctly from documents
- Check for state abbreviation differences (TN vs Tamil Nadu) - now handled automatically
- Ensure expected address matches the format in documents

### OCR Service Quota Exceeded

- Free tier limit: 100 pages per day
- Wait until next day or upgrade Unstract plan
- Error message will indicate quota exceeded

## Support

For issues or questions, check the application logs for detailed error messages.

