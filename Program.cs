using System.Text.Json;
using Microsoft.OpenApi.Models;
using QRCodeAPI.Configuration;
using QRCodeAPI.Middleware;
using QRCodeAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlaygroundDemoOptions>(
    builder.Configuration.GetSection(PlaygroundDemoOptions.SectionName));

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Access2Pay API",
        Version = "v1",
        Description = "Access2Pay APIs: InitiateProcess, GetProcessTickets, RouteProcessTicket. Most endpoints require the X-API-Key header."
    });

    // Only expose Access2Pay endpoints in Swagger UI.
    options.DocInclusionPredicate((_, apiDesc) =>
        apiDesc.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller)
        && string.Equals(controller, "Access2Pay", StringComparison.OrdinalIgnoreCase));

    // Xml docs → Swagger summary / description / parameter notes
    var xmlPath = Path.Combine(AppContext.BaseDirectory, "QRCodeAPI.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    // JsonElement has no default schema and can break swagger.json generation.
    options.MapType<JsonElement>(() => new OpenApiSchema
    {
        Type = "object",
        AdditionalPropertiesAllowed = true,
        Description = "JSON object body"
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Playground API key. Example: X-API-Key: your-key",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add HttpClientFactory for external API calls with timeout configuration
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180); // 3 minutes for LLM API calls
});

builder.Services.AddHttpClient("Unstract", client =>
{
    client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for OCR processing
});

builder.Services.AddHttpClient("InvoiceOcr", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Default HttpClient for other services
builder.Services.AddHttpClient();

// Register QrCodeService
builder.Services.AddScoped<QrCodeService>();

// Register FileSummaryService
builder.Services.AddScoped<FileSummaryService>();

builder.Services.AddScoped<FormDetailsService>();

builder.Services.AddScoped<Access2PayService>();
builder.Services.AddScoped<InvoiceOcrPipelineService>();

// Register KycAgentService
builder.Services.AddScoped<KycAgentService>();

// Register KYC Verification Services
builder.Services.AddScoped<DocumentProcessingService>();
builder.Services.AddScoped<AddressVerificationService>();
builder.Services.AddScoped<ConsistencyCheckService>();

// Register AWS Rekognition service first (required by FaceMatchingService)
builder.Services.AddScoped<AwsRekognitionMatchingService>();

// Register FaceMatchingService (wrapper that uses AWS Rekognition)
builder.Services.AddScoped<FaceMatchingService>();

builder.Services.AddScoped<KycVerificationService>();

builder.Services.AddScoped<ApiKeyService>();

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

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "EZOFIS API Playground v1");
    options.DocumentTitle = "EZOFIS API Playground";
    options.RoutePrefix = "swagger";
});

// Configure the HTTP request pipeline
app.UseCors("AllowAll");

// Enable static files for playground
app.UseStaticFiles();

// Add API Key middleware
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();

app.MapControllers();

// Default route to API key page
app.MapGet("/", () => Results.Redirect("/apikey.html?id=1"));

app.Run();
