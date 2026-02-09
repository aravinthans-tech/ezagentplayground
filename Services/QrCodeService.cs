// using Leadtools;
// using Leadtools.Barcode;
// using Leadtools.ImageProcessing;
using QRCodeAPI.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QRCodeAPI.Services;

public class QrCodeService
{
    private readonly IConfiguration _configuration;
    private readonly string _apiContentRootPath;

    public QrCodeService(IConfiguration configuration)
    {
        _configuration = configuration;
        _apiContentRootPath = _configuration["License:ContentRootPath"] ?? AppDomain.CurrentDomain.BaseDirectory;
    }

    // public static bool SetLicense()
    // {
    //     return SetLicense(false, AppDomain.CurrentDomain.BaseDirectory);
    // }

    // public static bool SetLicense(bool silent, string? apiContentRootPath = null)
    // {
    //     string rootPath = apiContentRootPath ?? AppDomain.CurrentDomain.BaseDirectory;
    //     
    //     if (RasterSupport.KernelExpired)
    //     {
    //         string dir = Path.Combine(rootPath, "Common");
    //         string licenseFileRelativePath = Path.Combine(dir, "Liscence", "LEADTOOLS.LIC");
    //         string keyFileRelativePath = Path.Combine(dir, "Liscence", "LEADTOOLS.LIC.key");

    //         if (System.IO.File.Exists(licenseFileRelativePath) && System.IO.File.Exists(keyFileRelativePath))
    //         {
    //             string developerKey = System.IO.File.ReadAllText(keyFileRelativePath);
    //             try
    //             {
    //                 RasterSupport.SetLicense(licenseFileRelativePath, developerKey);
    //             }
    //             catch (Exception ex)
    //             {
    //                 System.Diagnostics.Debug.Write(ex.Message);
    //             }
    //         }
    //     }

    //     if (RasterSupport.KernelExpired)
    //     {
    //         if (silent == false)
    //         {
    //             string msg = "Your license file is missing, invalid or expired. LEADTOOLS will not function. Please contact LEAD Sales for information on obtaining a valid license.";
    //             string logmsg = string.Format("*** NOTE: {0} ***{1}", msg, Environment.NewLine);
    //             System.Diagnostics.Debugger.Log(0, null, "*******************************************************************************" + Environment.NewLine);
    //             System.Diagnostics.Debugger.Log(0, null, logmsg);
    //             System.Diagnostics.Debugger.Log(0, null, "*******************************************************************************" + Environment.NewLine);

    //             System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    //             {
    //                 FileName = "https://www.leadtools.com/downloads/evaluation-form.asp?evallicenseonly=true",
    //                 UseShellExecute = true
    //             });
    //         }

    //         return false;
    //     }

    //     return true;
    // }

    // public bool InitLead()
    // {
    //     return SetLicense(false, _apiContentRootPath);
    // }

    public ResultForHttpsCode GenerateQrCode(string qrvalue)
    {
        var result = new ResultForHttpsCode();

        try
        {
            // var res = InitLead();
            // BarcodeEngine engine = new BarcodeEngine();
            // var recognizedCharacters = qrvalue;

            // var resolution = 100;
            // var WidthHeight = 125;
            // var image = RasterImage.Create(WidthHeight, WidthHeight, 1, resolution, RasterColor.FromKnownColor(RasterKnownColor.White));
            // var writer = engine.Writer;
            // var Data = BarcodeData.CreateDefaultBarcodeData(BarcodeSymbology.QR);
            // Data.Bounds = new LeadRect(0, 0, image.ImageWidth, image.ImageHeight);
            // var writeOptions = writer.GetDefaultOptions(Data.Symbology);
            // Data.Value = recognizedCharacters;
            // writer.CalculateBarcodeDataBounds(new LeadRect(0, 0, image.ImageWidth, image.ImageHeight), image.XResolution, image.YResolution, Data, writeOptions);
            // var s1 = (WidthHeight - Data.Bounds.Width) / 2;
            // Data.Bounds = new LeadRect(s1, s1, Data.Bounds.Width, Data.Bounds.Height);
            // writer.WriteBarcode(image, Data, writeOptions);

            // // Convert RasterImage to Bitmap by accessing pixel data
            // int width = image.Width;
            // int height = image.Height;
            // using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            // {
            //     // Lock the bitmap data
            //     BitmapData bitmapData = bitmap.LockBits(
            //         new Rectangle(0, 0, width, height),
            //         ImageLockMode.WriteOnly,
            //         PixelFormat.Format32bppArgb);

            //     try
            //     {
            //         int stride = bitmapData.Stride;
            //         byte[] bitmapBytes = new byte[stride * height];
            //         
            //         // Copy pixel data from RasterImage to bitmap
            //         for (int y = 0; y < height; y++)
            //         {
            //             for (int x = 0; x < width; x++)
            //             {
            //                 // Get pixel value from RasterImage (grayscale)
            //                 RasterColor color = image.GetPixelColor(x, y);
            //                 byte pixelValue = (byte)((color.R + color.G + color.B) / 3);
            //                 
            //                 int bitmapIndex = y * stride + x * 4;
            //                 
            //                 // Convert grayscale to RGBA
            //                 bitmapBytes[bitmapIndex] = pixelValue;     // B
            //                 bitmapBytes[bitmapIndex + 1] = pixelValue; // G
            //                 bitmapBytes[bitmapIndex + 2] = pixelValue; // R
            //                 bitmapBytes[bitmapIndex + 3] = 255;        // A
            //             }
            //         }
            //         
            //         // Copy to bitmap
            //         Marshal.Copy(bitmapBytes, 0, bitmapData.Scan0, bitmapBytes.Length);
            //     }
            //     finally
            //     {
            //         bitmap.UnlockBits(bitmapData);
            //     }

            //     // Save to PNG
            //     using (MemoryStream ms = new MemoryStream())
            //     {
            //         bitmap.Save(ms, ImageFormat.Png);
            //         byte[] buffer = ms.ToArray();
            //         result.output = Convert.ToBase64String(buffer);
            //         result.id = 1;
            //     }
            // }
        }
        catch (Exception ex)
        {
            result.EncryptOutput = "ERROR CODE:WDBR740F300DB30 " + ex.ToString();
            result.id = 0;
        }

        return result;
    }
}

