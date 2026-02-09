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

// Register FaceMatchingService - use factory pattern to handle potential OpenCV initialization failures
// OpenCV can throw TypeInitializationException during class loading, so we use lazy initialization
// The service will be created on first request, and if OpenCV fails, it will gracefully degrade
builder.Services.AddScoped<FaceMatchingService>(serviceProvider =>
{
    try
    {
        var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        return new FaceMatchingService(logger, configuration);
    }
    catch (TypeInitializationException ex)
    {
        // If OpenCV fails to load, create a service instance anyway (it will handle the error gracefully)
        var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        logger.LogError(ex, "OpenCV failed to initialize during FaceMatchingService creation. Face matching will be unavailable.");
        return new FaceMatchingService(logger, configuration);
    }
    catch (Exception ex)
    {
        // Catch any other exceptions
        var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        logger.LogWarning(ex, "Warning during FaceMatchingService creation. Face matching may be unavailable.");
        return new FaceMatchingService(logger, configuration);
    }
});

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

