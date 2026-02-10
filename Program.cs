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

// Register StubFaceMatchingService instead of FaceMatchingService to avoid OpenCV dependencies
// This allows testing if the app works without OpenCV
// To re-enable OpenCV, change this to: builder.Services.AddScoped<IFaceMatchingService, FaceMatchingService>();
builder.Services.AddScoped<IFaceMatchingService, StubFaceMatchingService>();

// Also register as FaceMatchingService for backward compatibility (will use stub)
builder.Services.AddScoped<FaceMatchingService>(serviceProvider =>
{
    var stubService = (StubFaceMatchingService)serviceProvider.GetRequiredService<IFaceMatchingService>();
    var logger = serviceProvider.GetRequiredService<ILogger<FaceMatchingService>>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    // Create a wrapper that extends FaceMatchingService but uses stub implementation
    return new FaceMatchingServiceWrapper(stubService, logger, configuration);
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

