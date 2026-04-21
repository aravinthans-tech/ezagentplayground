# QR Code Generation API

A RESTful API built with ASP.NET Core 8 for generating QR codes, featuring API key authentication and an interactive playground.

## Features

- **QR Code Generation**: Generate QR codes from text input
- **API Key Authentication**: Secure API access with X-API-Key header
- **Interactive Playground**: Test the API directly in your browser (no scrolling required)
- **Multiple Code Examples**: cURL, Python, and JavaScript examples
- **Base64 Image Response**: QR codes returned as base64-encoded PNG images

## Prerequisites

- .NET 8 SDK

## Setup

1. **Clone or download the project**

2. **Configure API Keys**

   Edit `appsettings.json` or `appsettings.Development.json`:

   ```json
   {
     "ApiKeys": {
       "ValidKeys": [
         "your-api-key-here",
         "another-key"
       ]
     }
   }
   ```

3. **Restore Dependencies**

   ```bash
   dotnet restore
   ```

4. **Run the Application**

   ```bash
   dotnet run
   ```

   The API will be available at `https://localhost:5001` or `http://localhost:5000`

5. **Access the Playground**

   Open your browser and navigate to:
   ```
   http://localhost:5000/index.html
   ```

## API Documentation

### Endpoint

**POST** `/api/qrcode/generate`

### Authentication

Include the API key in the request header:

```
X-API-Key: your-api-key-here
```

### Request Body

```json
{
  "qrvalue": "Hello World"
}
```

### Response

**Success Response (200 OK):**

```json
{
  "output": "iVBORw0KGgoAAAANSUhEUgAA...",
  "id": 1,
  "EncryptOutput": null
}
```

**Error Response (400 Bad Request / 500 Internal Server Error):**

```json
{
  "output": null,
  "id": 0,
  "EncryptOutput": "Error message here"
}
```

**Unauthorized Response (401 Unauthorized):**

```
Invalid API Key.
```

## Usage Examples

### cURL

```bash
curl -X POST "http://localhost:5000/api/qrcode/generate" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key-here" \
  -d '{"qrvalue": "Hello World"}'
```

### Python

```python
import requests

url = "http://localhost:5000/api/qrcode/generate"
headers = {
    "Content-Type": "application/json",
    "X-API-Key": "your-api-key-here"
}
data = {
    "qrvalue": "Hello World"
}

response = requests.post(url, json=data, headers=headers)
result = response.json()

if result["id"] == 1:
    # Decode base64 image
    import base64
    image_data = base64.b64decode(result["output"])
    with open("qrcode.png", "wb") as f:
        f.write(image_data)
    print("QR code saved as qrcode.png")
else:
    print(f"Error: {result['EncryptOutput']}")
```

### JavaScript

```javascript
const response = await fetch("http://localhost:5000/api/qrcode/generate", {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    "X-API-Key": "your-api-key-here"
  },
  body: JSON.stringify({
    qrvalue: "Hello World"
  })
});

const data = await response.json();

if (data.id === 1) {
  // Display image
  const img = document.createElement("img");
  img.src = `data:image/png;base64,${data.output}`;
  document.body.appendChild(img);
} else {
  console.error("Error:", data.EncryptOutput);
}
```

## Project Structure

```
QRCodeAPI/
├── Controllers/
│   └── QrCodeController.cs      # API controller
├── Models/
│   └── ResultForHttpsCode.cs    # Response model
├── Services/
│   └── QrCodeService.cs         # QR code generation logic
├── Middleware/
│   └── ApiKeyMiddleware.cs      # API key authentication
├── wwwroot/
│   └── index.html               # Interactive playground (single page, no scroll)
├── Program.cs                   # Application startup
├── appsettings.json             # Configuration
└── QRCodeAPI.csproj             # Project file
```

## FormDetails (Ezofis)

**POST** `/api/FormDetails`

Proxies `https://eztapi.ezofis.com/api/form/all` with optional AES wrapping via `encryptAES`.

- **Headers:** `X-API-Key` (your playground key), plus **`Authorization: Bearer <ezofis-jwt>`** or **`Ezofis-Token: <ezofis-jwt>`**
- **Body:** `{ "query": { ... }, "useEncryptedRequest": true, "useEncryptedResponse": true }`  
  Set `useEncryptedRequest` / `useEncryptedResponse` to `false` to send or receive raw JSON.

Optional config: `ExternalApis:Ezofis:BaseUrl` (default `https://eztapi.ezofis.com`).

Browser playground: open [`wwwroot/formdetails.html`](wwwroot/formdetails.html) at `/formdetails.html` (no API key required to load the page; the form sends `X-API-Key` when you submit).

## Configuration

### API Keys

Add valid API keys in `appsettings.json`:

```json
{
  "ApiKeys": {
    "ValidKeys": ["key1", "key2", "key3"]
  }
}
```

## Dependencies

- **System.Drawing.Common** (8.0.0) - Image conversion

## Troubleshooting

### API Key Not Working

1. Verify the API key is included in the `X-API-Key` header
2. Check that the API key exists in `appsettings.json` under `ApiKeys:ValidKeys`
3. Ensure the middleware is properly configured in `Program.cs`


