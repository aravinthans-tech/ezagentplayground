using Microsoft.AspNetCore.Http;
using OpenCvSharp;

namespace QRCodeAPI.Services;

public class FaceMatchingService : IDisposable
{
    private readonly ILogger<FaceMatchingService> _logger;
    private readonly IConfiguration _configuration;
    private CascadeClassifier? _faceCascade;

    private bool _isInitialized = false;
    private readonly object _initLock = new object();
    private bool _disposed = false;

    public FaceMatchingService(ILogger<FaceMatchingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        // Don't initialize OpenCV in constructor - use lazy initialization
    }

    private void EnsureInitialized()
    {
        if (_isInitialized) return;
        
        lock (_initLock)
        {
            if (_isInitialized) return;
            
            try
            {
                InitializeFaceCascade();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize OpenCvSharp. Face detection will be disabled.");
                _faceCascade = null;
                _isInitialized = true; // Mark as initialized even if failed, to prevent retry loops
            }
        }
    }

    private void InitializeFaceCascade()
    {
        try
        {
            // Try multiple locations for the cascade file
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "haarcascade_frontalface_default.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "haarcascade_frontalface_default.xml"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "haarcascade_frontalface_default.xml")
            };

            string? cascadePath = null;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    cascadePath = path;
                    break;
                }
            }

            if (cascadePath != null)
            {
                try
                {
                    _faceCascade = new CascadeClassifier(cascadePath);
                    if (_faceCascade.Empty())
                    {
                        _logger.LogWarning("Face cascade file is empty or invalid");
                        try
                        {
                            _faceCascade.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal errors during initialization
                        }
                        _faceCascade = null;
                    }
                    else
                    {
                        _logger.LogInformation("Face cascade loaded successfully from: {Path}", cascadePath);
                    }
                }
                catch (TypeInitializationException ex)
                {
                    _logger.LogError(ex.InnerException ?? ex, "OpenCV native libraries not available. Cannot create CascadeClassifier.");
                    _faceCascade = null;
                    throw; // Re-throw to be caught by EnsureInitialized
                }
                catch (DllNotFoundException ex)
                {
                    _logger.LogError(ex, "OpenCV DLL not found. Cannot create CascadeClassifier.");
                    _faceCascade = null;
                    throw; // Re-throw to be caught by EnsureInitialized
                }
            }
            else
            {
                _logger.LogWarning("Face cascade file not found. Face detection may not work. Please download haarcascade_frontalface_default.xml from OpenCV repository.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize face cascade");
            _faceCascade = null;
        }
    }

    public async Task<(byte[]? licenseFace, byte[]? selfieFace, bool match, int matchScore, string message)> ProcessAndCompare(
        IFormFile licenseImage, 
        IFormFile selfieImage)
    {
        // Lazy initialize OpenCV only when actually needed
        EnsureInitialized();
        
        // If OpenCV failed to initialize, return error message
        if (_faceCascade == null)
        {
            return (null, null, false, 0, "❌ Face matching is currently unavailable. OpenCV native libraries could not be loaded.");
        }
        
        try
        {
            // Read images
            byte[] licenseBytes;
            byte[] selfieBytes;

            using (var licenseStream = licenseImage.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await licenseStream.CopyToAsync(memoryStream);
                licenseBytes = memoryStream.ToArray();
            }

            using (var selfieStream = selfieImage.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await selfieStream.CopyToAsync(memoryStream);
                selfieBytes = memoryStream.ToArray();
            }

            // Detect and crop faces
            using var licenseFace = await AutoCropFace(licenseBytes);
            using var selfieFace = await AutoCropFace(selfieBytes);

            if (licenseFace == null || licenseFace.Empty())
            {
                return (null, null, false, 0, "❌ Photo Verification Failed<br>No face detected in license image.");
            }

            if (selfieFace == null || selfieFace.Empty())
            {
                byte[]? licenseFaceBytes = MatToByteArray(licenseFace);
                return (licenseFaceBytes, null, false, 0, "❌ Photo Verification Failed<br>No face detected in selfie image.");
            }

            // Preprocess faces
            using var licenseFaceProcessed = PreprocessFace(licenseFace);
            using var selfieFaceProcessed = PreprocessFace(selfieFace);

            // Compare faces using ORB
            var (matchScore, message) = CompareFacesOrb(licenseFaceProcessed, selfieFaceProcessed, matchCount: 5);
            var threshold = _configuration.GetValue<int>("KycVerification:FaceMatchThreshold", 4);
            var match = matchScore >= threshold;

            var resultMessage = match
                ? $"✅ Photo Verification Passed<br>Match Score: {matchScore}/5"
                : $"❌ Photo Verification Failed<br>Insufficient matches: {matchScore}/5 (Required: {threshold})";

            // Convert processed faces to byte arrays for return
            byte[]? processedLicenseFaceBytes = MatToByteArray(licenseFaceProcessed);
            byte[]? processedSelfieFaceBytes = MatToByteArray(selfieFaceProcessed);

            return (processedLicenseFaceBytes, processedSelfieFaceBytes, match, matchScore, resultMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in face matching");
            return (null, null, false, 0, $"❌ Error: {ex.Message}");
        }
    }

    private async Task<Mat?> AutoCropFace(byte[] imageBytes)
    {
        try
        {
            // Convert byte array to Mat
            Mat imgCv = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            
            if (imgCv.Empty())
            {
                _logger.LogWarning("Failed to decode image");
                return null;
            }

            // Ensure RGB format
            if (imgCv.Channels() == 4)
            {
                Cv2.CvtColor(imgCv, imgCv, ColorConversionCodes.BGRA2BGR);
            }

            Mat? bestCrop = null;
            int maxArea = 0;

            // Try 4 rotations: 0°, 90°, 180°, 270°
            var rotations = new[] { 0, 90, 180, 270 };

            foreach (var angle in rotations)
            {
                Mat rotated;
                if (angle == 0)
                {
                    rotated = imgCv.Clone();
                }
                else
                {
                    RotateFlags rotateFlag = angle switch
                    {
                        90 => RotateFlags.Rotate90Clockwise,
                        180 => RotateFlags.Rotate180,
                        270 => RotateFlags.Rotate90Counterclockwise,
                        _ => RotateFlags.Rotate90Clockwise
                    };
                    Cv2.Rotate(imgCv, rotated = new Mat(), rotateFlag);
                }

                // Convert to grayscale for face detection
                Mat gray = new Mat();
                Cv2.CvtColor(rotated, gray, ColorConversionCodes.BGR2GRAY);

                // Detect faces
                if (_faceCascade != null && !_faceCascade.Empty())
                {
                    Rect[] faces = _faceCascade.DetectMultiScale(
                        gray,
                        scaleFactor: 1.1,
                        minNeighbors: 4,
                        flags: HaarDetectionTypes.ScaleImage,
                        minSize: new OpenCvSharp.Size(30, 30)
                    );

                    // Find face with largest area
                    foreach (var face in faces)
                    {
                        int area = face.Width * face.Height;
                        if (area > maxArea)
                        {
                            maxArea = area;
                            bestCrop?.Dispose();
                            bestCrop = new Mat(rotated, face);
                        }
                    }
                }
                else
                {
                    // Fallback: use center region if cascade not available
                    if (angle == 0 && bestCrop == null)
                    {
                        int centerX = rotated.Width / 2;
                        int centerY = rotated.Height / 3;
                        int size = Math.Min(rotated.Width, rotated.Height) / 2;
                        int x = Math.Max(0, centerX - size / 2);
                        int y = Math.Max(0, centerY - size / 2);
                        int w = Math.Min(size, rotated.Width - x);
                        int h = Math.Min(size, rotated.Height - y);
                        bestCrop = new Mat(rotated, new Rect(x, y, w, h));
                    }
                }

                gray.Dispose();
                if (angle != 0)
                {
                    rotated.Dispose();
                }
            }

            imgCv.Dispose();

            return bestCrop;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not process face image");
            return null;
        }
    }

    private Mat PreprocessFace(Mat faceImg)
    {
        try
        {
            // Convert to grayscale
            Mat gray = new Mat();
            Cv2.CvtColor(faceImg, gray, ColorConversionCodes.BGR2GRAY);

            // Apply CLAHE (Contrast Limited Adaptive Histogram Equalization)
            using var clahe = CLAHE.Create(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            Mat eq = new Mat();
            clahe.Apply(gray, eq);

            // Convert back to BGR
            Mat eqBgr = new Mat();
            Cv2.CvtColor(eq, eqBgr, ColorConversionCodes.GRAY2BGR);

            // Resize to 128x128
            Mat resized = new Mat();
            Cv2.Resize(eqBgr, resized, new OpenCvSharp.Size(128, 128));

            gray.Dispose();
            eq.Dispose();
            eqBgr.Dispose();

            return resized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preprocessing face");
            // Return original resized if preprocessing fails
            Mat resized = new Mat();
            Cv2.Resize(faceImg, resized, new OpenCvSharp.Size(128, 128));
            return resized;
        }
    }

    private (int matchScore, string message) CompareFacesOrb(Mat img1, Mat img2, int matchCount = 5, double maxAvgDistance = 50)
    {
        try
        {
            // Create ORB detector
            using var orb = ORB.Create();

            // Detect keypoints and descriptors
            KeyPoint[] kp1, kp2;
            Mat des1 = new Mat();
            Mat des2 = new Mat();

            orb.DetectAndCompute(img1, null, out kp1, des1);
            orb.DetectAndCompute(img2, null, out kp2, des2);

            if (des1.Empty() || des2.Empty() || kp1.Length == 0 || kp2.Length == 0)
            {
                des1?.Dispose();
                des2?.Dispose();
                return (0, "Face features could not be extracted.");
            }

            // Use BFMatcher with Hamming distance
            using var bf = new BFMatcher(NormTypes.Hamming, crossCheck: false);
            DMatch[][] matches = bf.KnnMatch(des1, des2, k: 2);

            if (matches.Length == 0)
            {
                des1.Dispose();
                des2.Dispose();
                return (0, "No feature matches found.");
            }

            // Apply Lowe's ratio test (0.75 threshold)
            var good = new List<DMatch>();
            foreach (var matchPair in matches)
            {
                if (matchPair.Length == 2)
                {
                    var m = matchPair[0];
                    var n = matchPair[1];
                    if (m.Distance < 0.75 * n.Distance)
                    {
                        good.Add(m);
                    }
                }
            }

            // Sort by distance and take top matches
            good = good.OrderBy(m => m.Distance).Take(matchCount).ToList();

            int matchScore = good.Count;
            string message = matchScore < matchCount
                ? $"Insufficient matches: {matchScore}/{matchCount}."
                : $"Matches found: {matchScore}/{matchCount}.";

            des1.Dispose();
            des2.Dispose();

            return (matchScore, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing faces with ORB");
            return (0, $"Face comparison error: {ex.Message}");
        }
    }

    private byte[]? MatToByteArray(Mat mat)
    {
        try
        {
            if (mat.Empty())
                return null;

            // Encode Mat to PNG byte array
            Cv2.ImEncode(".png", mat, out byte[] imageBytes);
            return imageBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting Mat to byte array");
            return null;
        }
    }

    // Dispose pattern to properly clean up OpenCV resources and prevent finalizer crashes
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                if (_faceCascade != null)
                {
                    try
                    {
                        _faceCascade.Dispose();
                    }
                    catch (TypeInitializationException ex)
                    {
                        // Suppress finalizer exceptions - OpenCV native libraries may not be available
                        _logger.LogWarning(ex.InnerException ?? ex, "Error disposing CascadeClassifier (OpenCV may not be available). This is safe to ignore.");
                    }
                    catch (DllNotFoundException ex)
                    {
                        // Suppress DLL not found exceptions during disposal
                        _logger.LogWarning(ex, "OpenCV DLL not found during disposal. This is safe to ignore.");
                    }
                    catch (Exception ex)
                    {
                        // Log but don't throw - we're disposing, so errors are acceptable
                        _logger.LogWarning(ex, "Error disposing CascadeClassifier. This is safe to ignore.");
                    }
                    finally
                    {
                        _faceCascade = null;
                    }
                }
            }
            _disposed = true;
        }
    }

    // Finalizer - should not be called if Dispose is called, but added as safety net
    ~FaceMatchingService()
    {
        // Only dispose unmanaged resources in finalizer
        // CascadeClassifier is managed, so we just log if finalizer is called
        if (!_disposed)
        {
            _logger.LogWarning("FaceMatchingService finalizer called. This should not happen if Dispose was called properly.");
        }
    }
}
