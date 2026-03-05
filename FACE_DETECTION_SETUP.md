# Face Detection Setup Instructions

## Required File: haarcascade_frontalface_default.xml

The face detection feature requires the Haar Cascade classifier file for face detection.

### Download Instructions

1. **Download the cascade file:**
   - Go to: https://github.com/opencv/opencv/blob/master/data/haarcascades/haarcascade_frontalface_default.xml
   - Click "Raw" button to download the file
   - Save it as `haarcascade_frontalface_default.xml`

2. **Place the file in one of these locations:**
   - Project root directory (same folder as `QRCodeAPI.csproj`)
   - `wwwroot` folder
   - The application will automatically search for it in these locations

3. **File Properties (if using Visual Studio):**
   - Right-click the file in Solution Explorer
   - Select "Properties"
   - Set "Copy to Output Directory" to "Copy if newer"

### Alternative: Direct Download Command

You can download it directly using PowerShell:

```powershell
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml" -OutFile "haarcascade_frontalface_default.xml"
```

### Verification

After placing the file, restart the application. Check the logs for:
- `"Face cascade loaded successfully from: [path]"` - Success
- `"Face cascade file not found"` - File not found, check location

### Note

If the cascade file is not found, the system will use a fallback center-based face detection method, which may be less accurate.

