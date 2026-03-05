using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;

namespace QRCodeAPI.Controllers;

[ApiController]
[Route("api/Client")]
public class ApiKeyController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyController> _logger;

    public ApiKeyController(IConfiguration configuration, ILogger<ApiKeyController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("apiKey")]
    [ProducesDefaultResponseType(typeof(object))]
    public async Task<IActionResult> PostGenerateApikey([FromQuery] string userName, [FromQuery] string password)
    {
        string apiKey = "";
        string token = "";
        string tenantId = "";

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest("UserName and Password are required");
        }

        try
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(configPath);
            JObject appSettings = JObject.Parse(json);

            string connectionString = appSettings["ConnectionStrings"]?["eZApiTenantContext"]?.ToString() 
                ?? _configuration.GetConnectionString("eZApiTenantContext");

            if (string.IsNullOrEmpty(connectionString))
            {
                return StatusCode(500, "Database connection string not found");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // =========================
                // 1. Get Tenant Id
                // =========================
                using (SqlCommand cmdTenant = new SqlCommand(
                    "select top 1 id from tenant where email=@email", connection))
                {
                    cmdTenant.Parameters.AddWithValue("@email", userName);

                    var result = await cmdTenant.ExecuteScalarAsync();
                    if (result == null)
                        return BadRequest("Tenant is not created so please create tenant");

                    tenantId = result.ToString() ?? "";
                }

                // =========================
                // 2. Get Token
                // =========================
                using (SqlCommand cmdToken = new SqlCommand(
                    "select top 1 token from authenticate where userid=@uid and tenantId=@tid",
                    connection))
                {
                    cmdToken.Parameters.AddWithValue("@uid", tenantId);
                    cmdToken.Parameters.AddWithValue("@tid", tenantId);

                    var tok = await cmdToken.ExecuteScalarAsync();

                    // 🚨 If token not found → stop here
                    if (tok == null)
                        return BadRequest("Tenant is not created so please create tenant");

                    token = tok.ToString() ?? "";
                }

                // =========================
                // 3. Token exists → Get / Generate ApiKey
                // =========================
                using (SqlCommand cmdApi = new SqlCommand(
                    "select top 1 apikey from tenantuserApiKey where username=@u and password=@p",
                    connection))
                {
                    cmdApi.Parameters.AddWithValue("@u", userName);
                    cmdApi.Parameters.AddWithValue("@p", password);

                    var key = await cmdApi.ExecuteScalarAsync();

                    if (key != null)
                    {
                        apiKey = key.ToString() ?? "";
                    }
                    else
                    {
                        var bytes = RandomNumberGenerator.GetBytes(32);
                        apiKey = Convert.ToBase64String(bytes)
                                        .Replace("+", "")
                                        .Replace("/", "")
                                        .Replace("=", "");

                        using (SqlCommand cmdInsert = new SqlCommand(
                            @"insert into tenantuserApiKey
                              (username,password,apikey,createdby,createdat,tenantId)
                              values(@u,@p,@k,1,GETDATE(),@tid)", connection))
                        {
                            cmdInsert.Parameters.AddWithValue("@u", userName);
                            cmdInsert.Parameters.AddWithValue("@p", password);
                            cmdInsert.Parameters.AddWithValue("@k", apiKey);
                            cmdInsert.Parameters.AddWithValue("@tid", tenantId);

                            await cmdInsert.ExecuteNonQueryAsync();
                        }
                    }
                }
            }

            // =========================
            // 4. Return Token + ApiKey
            // =========================
            return Ok(new
            {
                tenantId = tenantId,
                token = token,
                apiKey = apiKey
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("apiKey")]
    [ProducesDefaultResponseType(typeof(object))]
    public async Task<IActionResult> GetApiKey([FromQuery] string userName, [FromQuery] string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest("UserName and Password are required");
        }

        try
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var json = System.IO.File.ReadAllText(configPath);
            JObject appSettings = JObject.Parse(json);

            string connectionString = appSettings["ConnectionStrings"]?["eZApiTenantContext"]?.ToString() 
                ?? _configuration.GetConnectionString("eZApiTenantContext");

            if (string.IsNullOrEmpty(connectionString))
            {
                return StatusCode(500, "Database connection string not found");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Check if API key exists for username/password
                using (SqlCommand cmdApi = new SqlCommand(
                    "select top 1 apikey, tenantId from tenantuserApiKey where username=@u and password=@p",
                    connection))
                {
                    cmdApi.Parameters.AddWithValue("@u", userName);
                    cmdApi.Parameters.AddWithValue("@p", password);

                    using (var reader = await cmdApi.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            var apiKey = reader["apikey"]?.ToString() ?? "";
                            var tenantId = reader["tenantId"]?.ToString() ?? "";

                            // Get token
                            string token = "";
                            using (var tokenConnection = new SqlConnection(connectionString))
                            {
                                await tokenConnection.OpenAsync();
                                using (SqlCommand cmdToken = new SqlCommand(
                                    "select top 1 token from authenticate where userid=@uid and tenantId=@tid",
                                    tokenConnection))
                                {
                                    cmdToken.Parameters.AddWithValue("@uid", tenantId);
                                    cmdToken.Parameters.AddWithValue("@tid", tenantId);
                                    var tok = await cmdToken.ExecuteScalarAsync();
                                    token = tok?.ToString() ?? "";
                                }
                            }

                            return Ok(new
                            {
                                tenantId = tenantId,
                                token = token,
                                apiKey = apiKey
                            });
                        }
                        else
                        {
                            return NotFound("API key not found for this user");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API key");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
