using V6Playground.Configuration;
using V6Playground.Middleware;
using V6Playground.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<V6ApiOptions>(
    builder.Configuration.GetSection(V6ApiOptions.SectionName));
builder.Services.Configure<SocialAuthOptions>(
    builder.Configuration.GetSection(SocialAuthOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PlaygroundKeyService>();
builder.Services.AddScoped<V6ApiClient>();

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

// IIS app under http://localhost/V6Playground
var pathBase = builder.Configuration["PathBase"]
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_PATHBASE");
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase.TrimEnd('/'));
}

app.UseCors("AllowAll");

// Serve apikey.html at app root (works with or without trailing slash under /V6Playground)
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("apikey.html");
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles();

app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();
app.MapControllers();

// ~/ respects PathBase so Location becomes /V6Playground/apikey.html (not /apikey.html)
app.MapGet("/", () => Results.Redirect("~/apikey.html"));

app.Run();
