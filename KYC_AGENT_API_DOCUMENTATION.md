# KYC Agent API Documentation

## Overview

The KYC Agent API provides comprehensive Know Your Customer (KYC) verification services. It processes documents, extracts KYC information, verifies addresses with Google Maps, checks document consistency, and performs face matching between license and selfie images.

## Base URL

```
http://localhost:5000/api/KycAgent
```

or

```
https://localhost:51347/api/KycAgent
```

## Authentication

All endpoints require API key authentication via the `X-API-Key` header:

```
X-API-Key: your-api-key-here
```

---

## Endpoints

### 1. Process Documents

**POST** `/api/KycAgent/process`

Extracts KYC data from one or more documents using OCR and optional face matching.

#### Request

**Content-Type:** `multipart/form-data`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `documents` | File[] | Yes | One or more document files (PDF, DOC, DOCX, TXT, JPG, JPEG, PNG) |
| `licenseImage` | File | No | License/ID image for face matching (JPG, JPEG, PNG) |
| `selfieImage` | File | No | Selfie image for face matching (JPG, JPEG, PNG) |

#### Response

**Success (200 OK):**
```json
{
  "id": 1,
  "output": "<html>KYC Agent Report with extracted data...</html>",
  "encryptOutput": null
}
```

**Error (400 Bad Request):**
```json
{
  "id": 0,
  "output": null,
  "encryptOutput": "At least one document is required"
}
```

#### Features

- Extracts text from documents using OCR (Unstract API)
- Extracts KYC fields (name, address, document type, etc.) using LLM
- Performs face matching if both license and selfie images are provided
- Returns HTML report with all extracted information

---

### 2. Verify Documents

**POST** `/api/KycAgent/verify`

Comprehensive document verification with address matching, consistency checks, and face verification.

#### Request

**Content-Type:** `multipart/form-data`

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `documents` | File[] | Yes | - | At least 2 document files (PDF, DOC, DOCX, TXT, JPG, JPEG, PNG) |
| `expectedAddress` | string | Yes | - | The expected address to match against extracted addresses |
| `modelChoice` | string | No | "Mistral" | LLM model: "Mistral" or "OpenAI" |
| `consistencyThreshold` | double | No | 0.82 | Address consistency threshold (0.0 to 1.0) |
| `licenseImage` | File | No | null | License/ID image for face matching (JPG, JPEG, PNG) |
| `selfieImage` | File | No | null | Selfie image for face matching (JPG, JPEG, PNG) |

#### Response Structure

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
        "date_of_birth": "1990-01-01"
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
  "statusHtml": "<div>Verification status HTML...</div>",
  "verificationTableHtml": "<div>Verification table HTML...</div>",
  "extractedFields": {
    "document_1": {},
    "document_2": {}
  }
}
```

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `finalResult` | boolean | Overall verification result (true = passed, false = failed) |
| `addressConsistencyScore` | double | Address similarity score between documents (0.0 to 1.0) |
| `nameConsistencyScore` | double | Name similarity score between documents (0.0 to 1.0) |
| `documentConsistencyScore` | double | Overall document consistency score |
| `averageAuthenticityScore` | double | Average authenticity score across all documents |
| `documentsConsistent` | boolean | Whether addresses from documents match each other |
| `documents` | array | Array of document verification results |
| `faceMatch` | object | Face matching result (if license and selfie provided) |
| `statusHtml` | string | HTML formatted verification status |
| `verificationTableHtml` | string | HTML formatted verification table |
| `extractedFields` | object | Extracted KYC fields from all documents |

#### Document Verification Object

| Field | Type | Description |
|-------|------|-------------|
| `documentIndex` | integer | Document number (1, 2, 3, ...) |
| `extractedAddress` | string | Address extracted from document |
| `extractedName` | string | Full name extracted from document |
| `similarityToExpected` | double | Similarity to expected address (0.0 to 1.0) |
| `addressMatch` | boolean | Whether address matches expected address |
| `googleMapsVerified` | boolean | Whether address was verified by Google Maps |
| `googleMapsFormattedAddress` | string | Google Maps formatted address |
| `authenticityScore` | double | Document authenticity score (0.0 to 1.0) |
| `extractedFields` | object | All extracted KYC fields from document |

#### Face Match Result Object

| Field | Type | Description |
|-------|------|-------------|
| `match` | boolean | Whether face match passed (score >= threshold) |
| `matchScore` | integer | Face match score (0 to 5) |
| `message` | string | HTML formatted message about face match result |
| `licenseFaceImage` | string | Base64 encoded processed license face image |
| `selfieFaceImage` | string | Base64 encoded processed selfie face image |

---

## Verification Process

### Step 1: Document Processing
1. OCR text extraction from all documents (parallel processing)
2. KYC field extraction using LLM (name, address, DOB, etc.)
3. Address extraction from each document

### Step 2: Address Consistency Check
1. Compare addresses from all documents
2. Normalize addresses (expand abbreviations: TN → Tamil Nadu)
3. Calculate similarity score using Jaccard similarity
4. **If addresses match**: Proceed to Google Maps verification
5. **If addresses don't match**: Skip Google Maps, mark as not verified

### Step 3: Google Maps Verification (if addresses match)
1. Verify each document's address with Google Maps Geocoding API
2. Get formatted address from Google Maps
3. Calculate authenticity score (similarity between extracted and Google formatted address)

### Step 4: Face Matching (if provided)
1. Detect and crop faces from license and selfie images
2. Preprocess images (CLAHE, grayscale, resize)
3. Compare faces using ORB feature matching
4. Calculate match score (0-5)
5. Pass if score >= 4 (configurable threshold)

### Step 5: Final Result Calculation
- All documents must have matching addresses
- Address consistency score >= threshold
- Face match must pass (if face images provided)
- Google Maps verification must pass (if addresses matched)

---

## Code Examples

### cURL - Process Documents

```bash
curl -X POST "http://localhost:5000/api/KycAgent/process" \
  -H "X-API-Key: your-api-key-here" \
  -F "documents=@document1.pdf" \
  -F "documents=@document2.pdf" \
  -F "licenseImage=@license.jpg" \
  -F "selfieImage=@selfie.jpg"
```

### cURL - Verify Documents

```bash
curl -X POST "http://localhost:5000/api/KycAgent/verify" \
  -H "X-API-Key: your-api-key-here" \
  -F "documents=@document1.pdf" \
  -F "documents=@document2.pdf" \
  -F "expectedAddress=10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004" \
  -F "modelChoice=Mistral" \
  -F "consistencyThreshold=0.82" \
  -F "licenseImage=@license.jpg" \
  -F "selfieImage=@selfie.jpg"
```

### Python - Process Documents

```python
import requests

url = "http://localhost:5000/api/KycAgent/process"
headers = {"X-API-Key": "your-api-key-here"}

files = [
    ("documents", ("doc1.pdf", open("doc1.pdf", "rb"), "application/pdf")),
    ("documents", ("doc2.pdf", open("doc2.pdf", "rb"), "application/pdf")),
    ("licenseImage", ("license.jpg", open("license.jpg", "rb"), "image/jpeg")),
    ("selfieImage", ("selfie.jpg", open("selfie.jpg", "rb"), "image/jpeg"))
]

response = requests.post(url, headers=headers, files=files)
result = response.json()

if result["id"] == 1:
    print("HTML Report:", result["output"])
else:
    print("Error:", result["encryptOutput"])
```

### Python - Verify Documents

```python
import requests

url = "http://localhost:5000/api/KycAgent/verify"
headers = {"X-API-Key": "your-api-key-here"}

files = [
    ("documents", ("doc1.pdf", open("doc1.pdf", "rb"), "application/pdf")),
    ("documents", ("doc2.pdf", open("doc2.pdf", "rb"), "application/pdf")),
    ("licenseImage", ("license.jpg", open("license.jpg", "rb"), "image/jpeg")),
    ("selfieImage", ("selfie.jpg", open("selfie.jpg", "rb"), "image/jpeg"))
]

data = {
    "expectedAddress": "10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004",
    "modelChoice": "Mistral",
    "consistencyThreshold": 0.82
}

response = requests.post(url, headers=headers, files=files, data=data)
result = response.json()

print(f"Verification: {'✅ Passed' if result['finalResult'] else '❌ Failed'}")
print(f"Address Consistency: {result['addressConsistencyScore']:.2%}")
print(f"Name Consistency: {result['nameConsistencyScore']:.2%}")

if result.get('faceMatch'):
    print(f"Face Match: {'✅ Passed' if result['faceMatch']['match'] else '❌ Failed'}")
    print(f"Face Score: {result['faceMatch']['matchScore']}/5")
```

### JavaScript (Browser) - Verify Documents

```javascript
async function verifyKYC() {
    const formData = new FormData();
    
    // Add documents
    const docInputs = document.querySelectorAll('input[type="file"][name="documents"]');
    docInputs.forEach(input => {
        if (input.files[0]) {
            formData.append('documents', input.files[0]);
        }
    });
    
    // Add expected address
    formData.append('expectedAddress', '10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004');
    formData.append('modelChoice', 'Mistral');
    formData.append('consistencyThreshold', '0.82');
    
    // Add face images
    const licenseInput = document.getElementById('licenseImage');
    const selfieInput = document.getElementById('selfieImage');
    if (licenseInput?.files[0]) formData.append('licenseImage', licenseInput.files[0]);
    if (selfieInput?.files[0]) formData.append('selfieImage', selfieInput.files[0]);
    
    const response = await fetch('http://localhost:5000/api/KycAgent/verify', {
        method: 'POST',
        headers: { 'X-API-Key': 'your-api-key-here' },
        body: formData
    });
    
    const result = await response.json();
    console.log('Verification Result:', result);
    return result;
}
```

### C# - Verify Documents

```csharp
using System.Net.Http;
using System.Text.Json;

var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-API-Key", "your-api-key-here");

var formData = new MultipartFormDataContent();

// Add documents
formData.Add(new ByteArrayContent(File.ReadAllBytes("doc1.pdf")), "documents", "doc1.pdf");
formData.Add(new ByteArrayContent(File.ReadAllBytes("doc2.pdf")), "documents", "doc2.pdf");

// Add expected address
formData.Add(new StringContent("10 F2 Narayanasamy Kovil Street, Pettai, Tirunelveli, Tamil Nadu 627004"), "expectedAddress");
formData.Add(new StringContent("Mistral"), "modelChoice");
formData.Add(new StringContent("0.82"), "consistencyThreshold");

// Add face images
if (File.Exists("license.jpg"))
    formData.Add(new ByteArrayContent(File.ReadAllBytes("license.jpg")), "licenseImage", "license.jpg");
if (File.Exists("selfie.jpg"))
    formData.Add(new ByteArrayContent(File.ReadAllBytes("selfie.jpg")), "selfieImage", "selfie.jpg");

var response = await client.PostAsync("http://localhost:5000/api/KycAgent/verify", formData);
var result = JsonSerializer.Deserialize<KycVerificationResult>(await response.Content.ReadAsStringAsync());
```

---

## Features

### Address Verification Logic

1. **Address Consistency Check First**: Documents are checked for address consistency before Google Maps verification
2. **Conditional Google Maps Verification**: Google Maps API is only called if addresses from documents match
3. **Address Normalization**: State/province abbreviations are automatically expanded (TN → Tamil Nadu, ON → Ontario)
4. **Similarity Calculation**: Uses Jaccard similarity with normalized addresses

### Face Matching

- **Technology**: Azure Face API (cloud-based face recognition service)
- **Preprocessing**: CLAHE (Contrast Limited Adaptive Histogram Equalization)
- **Threshold**: Minimum score of 4/5 required to pass (configurable)
- **Rotation Handling**: Automatically handles rotated images (0°, 90°, 180°, 270°)

### Performance Optimizations

- **Parallel Processing**: Documents processed in parallel
- **Concurrent Operations**: OCR, LLM calls, and Google Maps verification run concurrently
- **Early Face Matching**: Face matching starts in parallel with document processing
- **Optimized OCR Polling**: Reduced polling interval for faster results

---

## Configuration

### appsettings.json

```json
{
  "KycVerification": {
    "FaceMatchThreshold": 4,
    "ConsistencyThreshold": 0.82
  },
  "ExternalApis": {
    "Unstract": {
      "ApiKey": "your-unstract-api-key",
      "BaseUrl": "https://api.unstract.com"
    },
    "OpenRouter": {
      "ApiKey": "your-openrouter-api-key",
      "BaseUrl": "https://openrouter.ai/api/v1"
    },
    "GoogleMaps": {
      "ApiKey": "your-google-maps-api-key",
      "GeocodingUrl": "https://maps.googleapis.com/maps/api/geocode/json"
    }
  }
}
```

---

## Error Handling

### Common Error Responses

**400 Bad Request:**
- "At least one document is required" (process endpoint)
- "Please upload at least two documents" (verify endpoint)
- "Expected address is required" (verify endpoint)

**401 Unauthorized:**
- "API Key was not provided"
- "Invalid API Key"

**500 Internal Server Error:**
- OCR service errors
- LLM API errors
- Google Maps API errors
- Face matching errors

### Error Response Format

```json
{
  "id": 0,
  "output": null,
  "encryptOutput": "Error message here"
}
```

or for verify endpoint:

```json
{
  "statusHtml": "❌ <b style='color:red;'>Error message</b>",
  "finalResult": false
}
```

---

## Best Practices

1. **Document Quality**: Ensure documents are clear and readable for better OCR accuracy
2. **Address Format**: Provide expected address in consistent format
3. **Face Images**: Use clear, front-facing photos for better face matching
4. **API Key Security**: Never expose API keys in client-side code
5. **Error Handling**: Always check response status and handle errors appropriately
6. **File Size**: Keep document file sizes reasonable for faster processing

---

## Limitations

1. **OCR Quota**: Free tier has 100 pages per day limit (Unstract)
2. **Google Maps**: Requires valid API key with Geocoding API enabled
3. **Face Matching**: Requires clear, front-facing faces in images
4. **HTTPS Required**: Camera access requires HTTPS (except localhost)

---

## Support

For issues or questions, check application logs for detailed error messages.

