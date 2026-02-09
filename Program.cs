using QRCodeAPI.Middleware;
using QRCodeAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add HttpClientFactory for external API calls with timeout configuration
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180); // 3 minutes for LLM API calls
});

builder.Services.AddHttpClient("Unstract", client =>
{
    client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for OCR processing
});

// Default HttpClient for other services
builder.Services.AddHttpClient();

// Register QrCodeService
builder.Services.AddScoped<QrCodeService>();

// Register FileSummaryService
builder.Services.AddScoped<FileSummaryService>();

// Register KycAgentService
builder.Services.AddScoped<KycAgentService>();

// Register KYC Verification Services
builder.Services.AddScoped<DocumentProcessingService>();
builder.Services.AddScoped<AddressVerificationService>();
builder.Services.AddScoped<ConsistencyCheckService>();

// Register FaceMatchingService conditionally - handle OpenCV loading failures gracefully
// TypeInitializationException can occur during class loading, so we wrap the entire registration
try
{
    builder.Services.AddScoped<FaceMatchingService?>(serviceProvider =>
    {
        try
        {
            // Try to create the service - if OpenCV isn't available, this will fail gracefully
            var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var service = new FaceMatchingService(logger, configuration);
            return service;
        }
        catch (TypeInitializationException ex)
        {
            // OpenCV native libraries not available - return null instead of crashing
            var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
            logger.LogWarning(ex.InnerException ?? ex, "OpenCV native libraries not available. Face matching will be unavailable.");
            return null;
        }
        catch (DllNotFoundException ex)
        {
            // OpenCV DLL not found - return null
            var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
            logger.LogWarning(ex, "OpenCV DLL not found. Face matching will be unavailable.");
            return null;
        }
        catch (Exception ex)
        {
            // Other exceptions - log and return null
            var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
            logger.LogWarning(ex, "Failed to create FaceMatchingService. Face matching will be unavailable.");
            return null;
        }
    });
}
catch (TypeInitializationException)
{
    // If OpenCV fails during class loading (when JIT compiles the class), skip registration entirely
    // This prevents the app from crashing - KycVerificationService will handle null FaceMatchingService
    Console.WriteLine("WARNING: OpenCV failed to load during class initialization. FaceMatchingService will not be available.");
}
catch (Exception ex)
{
    // Log any other errors but don't crash
    Console.WriteLine($"WARNING: Could not register FaceMatchingService: {ex.Message}");
}

builder.Services.AddScoped<KycVerificationService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Early check for OpenCV availability - log warning if not available but don't crash
try
{
    // Try to reference FaceMatchingService type to see if OpenCV can be loaded
    var faceMatchingType = typeof(FaceMatchingService);
    app.Logger.LogInformation("FaceMatchingService type loaded successfully");
}
catch (TypeInitializationException ex)
{
    app.Logger.LogWarning(ex.InnerException ?? ex, "OpenCV native libraries not available. Face matching will be unavailable.");
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not verify FaceMatchingService availability. Face matching may be unavailable.");
}

// Configure the HTTP request pipeline
app.UseCors("AllowAll");

// Enable static files for playground
app.UseStaticFiles();

// Add API Key middleware
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();

app.MapControllers();

// Default route to API key page
app.MapGet("/", () => Results.Redirect("/apikey.html"));

app.Run();

