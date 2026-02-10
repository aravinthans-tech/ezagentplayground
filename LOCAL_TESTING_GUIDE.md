# Local Testing Guide for InsightFace Face Matching

This guide explains how to test the InsightFace face matching implementation locally.

## Prerequisites

1. **Python 3.9+** installed on your system
2. **.NET 8.0 SDK** installed
3. Two test images with faces (license/ID photo and selfie)

## Step 1: Install Python Dependencies

Open a terminal in the project root and navigate to the `face_service` directory:

```bash
cd face_service
pip install -r requirements.txt
```

**Note:** InsightFace will automatically download model files on first run (this may take a few minutes).

## Step 2: Start Python InsightFace Service

In the `face_service` directory, run:

```bash
python -m uvicorn app:app --host 0.0.0.0 --port 5001
```

You should see output like:
```
INFO:     Started server process
INFO:     Waiting for application startup.
INFO:     Application startup complete.
INFO:     Uvicorn running on http://0.0.0.0:5001
```

**Keep this terminal window open** - the Python service must be running.

## Step 3: Verify Python Service is Working

Open a new terminal and test the health endpoint:

```bash
curl http://localhost:5001/health
```

Or visit in browser: `http://localhost:5001/health`

You should see:
```json
{
  "status": "healthy",
  "insightface_loaded": true
}
```

## Step 4: Start .NET Application

Open a **new terminal** in the project root directory and run:

```bash
dotnet run
```

The application should start on `http://localhost:5000` (or the port shown in the output).

## Step 5: Test Face Matching

### Option A: Using the KYC Agent API

The face matching is integrated into the KYC verification endpoint:

**Endpoint:** `POST /api/KycAgent/verify`

**Headers:**
```
X-API-Key: dev-api-key-1235
Content-Type: multipart/form-data
```

**Form Data:**
- `documents`: At least 2 document files
- `licenseImage`: License/ID photo with face
- `selfieImage`: Selfie photo with face

**Example using curl:**
```bash
curl -X POST http://localhost:5000/api/KycAgent/verify \
  -H "X-API-Key: dev-api-key-1235" \
  -F "documents=@document1.pdf" \
  -F "documents=@document2.pdf" \
  -F "licenseImage=@license.jpg" \
  -F "selfieImage=@selfie.jpg"
```

### Option B: Using the Web UI

1. Open browser: `http://localhost:5000/kycagent.html`
2. Enter API key: `dev-api-key-1235`
3. Upload documents and face images
4. Click "Verify KYC"
5. Check the face matching results in the response

## Step 6: Test Python Service Directly (Optional)

You can also test the Python service directly:

### Detect Face:
```bash
curl -X POST http://localhost:5001/detect \
  -F "file=@your_image.jpg"
```

### Verify Faces:
```bash
curl -X POST http://localhost:5001/verify \
  -H "Content-Type: application/json" \
  -d '{
    "faceId1": "base64_encoded_face_id_1",
    "faceId2": "base64_encoded_face_id_2"
  }'
```

## Troubleshooting

### Python Service Won't Start

1. **Check Python version:**
   ```bash
   python --version  # Should be 3.9+
   ```

2. **Reinstall dependencies:**
   ```bash
   cd face_service
   pip install --upgrade -r requirements.txt
   ```

3. **Check if port 5001 is in use:**
   ```bash
   # Windows
   netstat -ano | findstr :5001
   
   # Linux/Mac
   lsof -i :5001
   ```

### .NET Can't Connect to Python Service

1. **Verify Python service is running:**
   ```bash
   curl http://localhost:5001/health
   ```

2. **Check appsettings.json:**
   ```json
   "InsightFace": {
     "ServiceUrl": "http://localhost:5001"
   }
   ```

3. **Check .NET logs** for connection errors

### Face Detection Fails

1. **Check Python service logs** for InsightFace initialization errors
2. **Verify images contain clear faces**
3. **Check image formats** (JPG, PNG supported)

### InsightFace Models Not Downloading

InsightFace automatically downloads models on first run. If it fails:

1. Check internet connection
2. Models are downloaded to: `~/.insightface/models/` (or similar)
3. You can manually download models if needed

## Expected Results

When face matching works correctly, you should see:

- **Python service logs:** Face detection and verification messages
- **.NET logs:** HTTP calls to Python service, face matching results
- **API response:** Face match score (0-5) and match status

Example successful response:
```json
{
  "faceMatch": {
    "match": true,
    "matchScore": 5,
    "message": "✅ Photo Verification Passed<br>Match Score: 5/5 (Confidence: 95%)"
  }
}
```

## Stopping Services

1. Stop .NET: Press `Ctrl+C` in the .NET terminal
2. Stop Python: Press `Ctrl+C` in the Python terminal

## Next Steps

Once local testing is successful, you can:
1. Build Docker image: `docker build -t qrcodeapi .`
2. Test Docker container locally
3. Deploy to Render

