using System.Text.Json;

namespace QRCodeAPI.Services;

public class AddressVerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AddressVerificationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _googleMapsApiKey;
    private readonly string _geocodingUrl;

    public AddressVerificationService(
        IHttpClientFactory httpClientFactory,
        ILogger<AddressVerificationService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _googleMapsApiKey = _configuration["ExternalApis:GoogleMaps:ApiKey"] ?? string.Empty;
        _geocodingUrl = _configuration["ExternalApis:GoogleMaps:GeocodingUrl"] ?? "https://maps.googleapis.com/maps/api/geocode/json";
    }

    public async Task<(bool verified, string formattedAddress)> VerifyAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(_googleMapsApiKey))
        {
            _logger.LogWarning("Google Maps API key not configured");
            return (false, address);
        }

        if (string.IsNullOrWhiteSpace(address) || address.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Address is empty or 'None', skipping Google Maps verification");
            return (false, address);
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            // Removed region=ca restriction to allow international addresses (India, Canada, etc.)
            var url = $"{_geocodingUrl}?address={Uri.EscapeDataString(address)}&key={_googleMapsApiKey}";

            _logger.LogInformation("Verifying address with Google Maps: {Address}", address);

            var response = await httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    _logger.LogError("Google Maps API service temporarily unavailable (503). This is usually temporary. Error: {Error}", errorContent);
                    return (false, address);
                }
                
                _logger.LogError("Google Maps API returned error status {StatusCode}: {Error}", response.StatusCode, errorContent);
                return (false, address);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            if (!json.TryGetProperty("status", out var statusProp))
            {
                _logger.LogWarning("Google Maps response missing 'status' field");
                return (false, address);
            }

            var status = statusProp.GetString();
            _logger.LogInformation("Google Maps API status: {Status}", status);

            if (status == "OK" && json.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var firstResult = results[0];
                if (firstResult.TryGetProperty("formatted_address", out var formattedAddressProp))
                {
                    var formattedAddress = formattedAddressProp.GetString() ?? address;
                    _logger.LogInformation("Address verified successfully. Formatted: {FormattedAddress}", formattedAddress);
                    return (true, formattedAddress);
                }
            }
            else if (status == "ZERO_RESULTS")
            {
                _logger.LogWarning("Google Maps found no results for address: {Address}", address);
            }
            else if (status == "OVER_QUERY_LIMIT")
            {
                _logger.LogError("Google Maps API quota exceeded");
            }
            else if (status == "REQUEST_DENIED")
            {
                string errorMessage = "Google Maps API request denied.";
                if (json.TryGetProperty("error_message", out var errorMessageProp))
                {
                    errorMessage = errorMessageProp.GetString() ?? errorMessage;
                    _logger.LogError("Google Maps API REQUEST_DENIED: {ErrorMessage}", errorMessage);
                }
                else
                {
                    _logger.LogError("Google Maps API request denied. Check API key and billing.");
                }
                
                // Check if it's an API not enabled error
                if (errorMessage.Contains("not activated") || errorMessage.Contains("enable this API"))
                {
                    _logger.LogError("Geocoding API is not enabled. Please enable it in Google Cloud Console: https://console.cloud.google.com/apis/library/geocoding-backend.googleapis.com");
                }
            }
            else
            {
                _logger.LogWarning("Google Maps API returned status: {Status} for address: {Address}", status, address);
            }

            return (false, address);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parsing Google Maps JSON response");
            return (false, address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying address with Google Maps: {Address}", address);
            return (false, address);
        }
    }
}

