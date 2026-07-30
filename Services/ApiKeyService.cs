using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using QRCodeAPI.Models;

namespace QRCodeAPI.Services;

public class ApiKeyService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(IConfiguration configuration, ILogger<ApiKeyService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string? GetConnectionString() =>
        _configuration.GetConnectionString("eZApiTenantContext");

    public async Task<(string TenantId, string Token)?> GetTenantAndTokenAsync(string userName, SqlConnection connection)
    {
        var tenantId = await ResolveTenantIdByEmailAsync(userName, connection);
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        var token = await GetTokenByTenantIdAsync(tenantId, connection);
        if (token == null) return null;
        return (tenantId, token);
    }

    /// <summary>
    /// Resolve tenant id from <c>tenant.email</c>, or if missing from <c>tenantUser.email</c>.
    /// </summary>
    public async Task<string?> ResolveTenantIdByEmailAsync(string email, SqlConnection connection)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        using (var cmdTenant = new SqlCommand(
            "SELECT TOP 1 id FROM tenant WHERE email=@email", connection))
        {
            cmdTenant.Parameters.AddWithValue("@email", email.Trim());
            var result = await cmdTenant.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                var id = result.ToString();
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }
        }

        // Tenant admins live in tenant; other users live in tenantUser with a tenantId FK.
        using (var cmdTenantUser = new SqlCommand(
            @"SELECT TOP 1 tenantId
              FROM tenantUser
              WHERE email=@email", connection))
        {
            cmdTenantUser.Parameters.AddWithValue("@email", email.Trim());
            var result = await cmdTenantUser.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                var id = result.ToString();
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }
        }

        return null;
    }

    public async Task<string?> GetTokenByTenantIdAsync(string tenantId, SqlConnection? existing = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        async Task<string?> Run(SqlConnection conn)
        {
            using (var cmdToken = new SqlCommand(
                "SELECT TOP 1 token FROM authenticate WHERE userid=@uid AND tenantId=@tid", conn))
            {
                cmdToken.Parameters.AddWithValue("@uid", tenantId);
                cmdToken.Parameters.AddWithValue("@tid", tenantId);
                var tok = await cmdToken.ExecuteScalarAsync();
                if (tok != null && tok != DBNull.Value)
                {
                    var value = tok.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            // Tenant users: authenticate.userid may be a user id, not the tenant id.
            using (var cmdAny = new SqlCommand(
                "SELECT TOP 1 token FROM authenticate WHERE tenantId=@tid ORDER BY id DESC", conn))
            {
                cmdAny.Parameters.AddWithValue("@tid", tenantId);
                var tok = await cmdAny.ExecuteScalarAsync();
                return tok?.ToString();
            }
        }

        if (existing != null) return await Run(existing);

        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return null;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        return await Run(connection);
    }

    /// <summary>
    /// Resolve Ezofis JWT from playground API key:
    /// tenantuserApiKey.username (email) → tenant.connectionString → [user].id → authenticate.token.
    /// [user] is in the tenant database; authenticate is in ezEnterpriseMain.
    /// </summary>
    public async Task<(string? Token, string? Email, string? UserId, string? TenantId, string? Error)>
        GetEzofisTokenByApiKeyAsync(string apiKey, string? preferredTenantId = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (null, null, null, null, "API key is required");

        var mainCs = GetConnectionString();
        if (string.IsNullOrEmpty(mainCs))
            return (null, null, null, null, "Database not configured");

        try
        {
            await using var main = new SqlConnection(mainCs);
            await main.OpenAsync();

            string? email = null;
            string? keyTenantId = null;
            using (var cmdKey = new SqlCommand(
                @"SELECT TOP 1 username, tenantId
                  FROM tenantuserApiKey
                  WHERE apikey=@k", main))
            {
                cmdKey.Parameters.AddWithValue("@k", apiKey.Trim());
                await using var reader = await cmdKey.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (null, null, null, null, "API key not found");

                email = reader["username"]?.ToString()?.Trim();
                keyTenantId = reader["tenantId"] == DBNull.Value ? null : reader["tenantId"]?.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(email))
                return (null, null, null, null, "API key has no username/email");

            var tenantId = !string.IsNullOrWhiteSpace(preferredTenantId)
                ? preferredTenantId.Trim()
                : (!string.IsNullOrWhiteSpace(keyTenantId) ? keyTenantId : "2");

            string? tenantConnectionString = null;
            using (var cmdTenant = new SqlCommand(
                @"SELECT TOP 1 connectionString
                  FROM tenant
                  WHERE id=@tid AND (isDeleted=0 OR isDeleted IS NULL)", main))
            {
                cmdTenant.Parameters.AddWithValue("@tid", tenantId);
                var csVal = await cmdTenant.ExecuteScalarAsync();
                tenantConnectionString = csVal?.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(tenantConnectionString))
                return (null, email, null, tenantId, $"No connectionString on tenant id={tenantId}");

            string? userId = null;
            await using (var tenantDb = new SqlConnection(tenantConnectionString))
            {
                await tenantDb.OpenAsync();
                using var cmdUser = new SqlCommand(
                    @"SELECT TOP 1 id
                      FROM [user]
                      WHERE email=@email AND (isDeleted=0 OR isDeleted IS NULL)", tenantDb);
                cmdUser.Parameters.AddWithValue("@email", email);
                var result = await cmdUser.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    userId = result.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                // Fallback: tenantUser id on main DB (some tenants mirror ids here)
                using var cmdTu = new SqlCommand(
                    @"SELECT TOP 1 id
                      FROM tenantUser
                      WHERE email=@email AND tenantId=@tid", main);
                cmdTu.Parameters.AddWithValue("@email", email);
                cmdTu.Parameters.AddWithValue("@tid", tenantId);
                var tu = await cmdTu.ExecuteScalarAsync();
                if (tu != null && tu != DBNull.Value)
                    userId = tu.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(userId))
                return (null, email, null, tenantId, $"No [user] row found for email '{email}' in tenant DB {tenantId}");

            string? token = null;
            using (var cmdToken = new SqlCommand(
                @"SELECT TOP 1 token
                  FROM authenticate
                  WHERE tenantId=@tid AND userid=@uid
                  ORDER BY id DESC", main))
            {
                cmdToken.Parameters.AddWithValue("@tid", tenantId);
                cmdToken.Parameters.AddWithValue("@uid", userId);
                var tok = await cmdToken.ExecuteScalarAsync();
                if (tok != null && tok != DBNull.Value)
                    token = tok.ToString()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(token))
                return (null, email, userId, tenantId,
                    $"No authenticate token for tenantId={tenantId} and userid={userId}");

            return (token, email, userId, tenantId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetEzofisTokenByApiKeyAsync failed");
            return (null, null, null, null, ex.Message);
        }
    }

    public async Task<bool> UserOwnsApiKeyAsync(string userName, string password, int apiKeyId, SqlConnection? existing = null)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return false;

        async Task<bool> Run(SqlConnection conn)
        {
            using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM tenantuserApiKey WHERE id=@id AND username=@u AND password=@p", conn);
            cmd.Parameters.AddWithValue("@id", apiKeyId);
            cmd.Parameters.AddWithValue("@u", userName);
            cmd.Parameters.AddWithValue("@p", password);
            var n = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            return n > 0;
        }

        if (existing != null) return await Run(existing);
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        return await Run(connection);
    }

    public async Task<ApiKeyGenerateResult?> GenerateApiKeyAsync(
        string userName, string password, int daysValid, string? keyLabel = null)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return null;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var tenantInfo = await GetTenantAndTokenAsync(userName, connection);
        if (tenantInfo == null) return null;

        var (tenantId, token) = tenantInfo.Value;
        var bytes = RandomNumberGenerator.GetBytes(32);
        var apiKey = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");

        DateTime? expiresAt = daysValid > 0 ? DateTime.UtcNow.AddDays(daysValid) : null;
        var label = string.IsNullOrWhiteSpace(keyLabel)
            ? "Key " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
            : keyLabel.Trim();

        int apiKeyId;
        using (var cmdInsert = new SqlCommand(
            @"INSERT INTO tenantuserApiKey
              (username, password, apikey, createdby, createdat, tenantId, IsEnabled, ExpiresAt, KeyLabel)
              OUTPUT INSERTED.id
              VALUES (@u, @p, @k, 1, GETDATE(), @tid, 1, @exp, @lbl)", connection))
        {
            cmdInsert.Parameters.AddWithValue("@u", userName);
            cmdInsert.Parameters.AddWithValue("@p", password);
            cmdInsert.Parameters.AddWithValue("@k", apiKey);
            cmdInsert.Parameters.AddWithValue("@tid", tenantId);
            cmdInsert.Parameters.AddWithValue("@exp", (object?)expiresAt ?? DBNull.Value);
            cmdInsert.Parameters.AddWithValue("@lbl", label);
            apiKeyId = Convert.ToInt32(await cmdInsert.ExecuteScalarAsync());
        }

        return new ApiKeyGenerateResult
        {
            TenantId = tenantId,
            Token = token,
            ApiKey = apiKey,
            ApiKeyId = apiKeyId,
            ExpiresAt = expiresAt,
            IsNew = true
        };
    }

    public async Task<ApiKeyGenerateResult?> GetLatestApiKeyAsync(string userName, string password)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return null;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var tenantInfo = await GetTenantAndTokenAsync(userName, connection);
        if (tenantInfo == null) return null;

        var (tenantId, token) = tenantInfo.Value;

        using var cmd = new SqlCommand(
            @"SELECT TOP 1 id, apikey, ExpiresAt
              FROM tenantuserApiKey
              WHERE username=@u AND password=@p
                AND (IsEnabled IS NULL OR IsEnabled = 1)
                AND (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE())
              ORDER BY createdat DESC", connection);
        cmd.Parameters.AddWithValue("@u", userName);
        cmd.Parameters.AddWithValue("@p", password);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.Read()) return null;

        return new ApiKeyGenerateResult
        {
            TenantId = tenantId,
            Token = token,
            ApiKey = reader["apikey"]?.ToString() ?? "",
            ApiKeyId = Convert.ToInt32(reader["id"]),
            ExpiresAt = reader["ExpiresAt"] == DBNull.Value ? null : Convert.ToDateTime(reader["ExpiresAt"]),
            IsNew = false
        };
    }

    public async Task<IReadOnlyList<ApiKeyRecordDto>> ListApiKeysAsync(string userName, string password)
    {
        var list = new List<ApiKeyRecordDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT k.id, k.apikey, k.tenantId, k.KeyLabel, k.IsEnabled, k.ExpiresAt, k.createdat,
                     ISNULL(u.cnt, 0) AS TotalCalls
              FROM tenantuserApiKey k
              LEFT JOIN (
                  SELECT ApiKeyId, COUNT(*) AS cnt
                  FROM tenantApiKeyUsageLog
                  GROUP BY ApiKeyId
              ) u ON u.ApiKeyId = k.id
              WHERE k.username=@u AND k.password=@p
              ORDER BY
                CASE WHEN k.ExpiresAt IS NULL OR k.ExpiresAt > GETUTCDATE() THEN 0 ELSE 1 END,
                k.createdat DESC", connection);
        cmd.Parameters.AddWithValue("@u", userName);
        cmd.Parameters.AddWithValue("@p", password);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ApiKeyRecordDto
            {
                Id = Convert.ToInt32(reader["id"]),
                ApiKey = reader["apikey"]?.ToString() ?? "",
                TenantId = reader["tenantId"]?.ToString() ?? "",
                KeyLabel = reader["KeyLabel"] == DBNull.Value ? null : reader["KeyLabel"]?.ToString(),
                IsEnabled = reader["IsEnabled"] != DBNull.Value && Convert.ToBoolean(reader["IsEnabled"]),
                ExpiresAt = reader["ExpiresAt"] == DBNull.Value ? null : Convert.ToDateTime(reader["ExpiresAt"]),
                CreatedAt = Convert.ToDateTime(reader["createdat"]),
                TotalCalls = Convert.ToInt32(reader["TotalCalls"])
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<ApiKeyRecordDto>> ListApiKeysByEmailAsync(string userName)
    {
        var list = new List<ApiKeyRecordDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        using var cmd = new SqlCommand(
            @"SELECT k.id, k.apikey, k.tenantId, k.KeyLabel, k.IsEnabled, k.ExpiresAt, k.createdat,
                     ISNULL(u.cnt, 0) AS TotalCalls
              FROM tenantuserApiKey k
              LEFT JOIN (
                  SELECT ApiKeyId, COUNT(*) AS cnt
                  FROM tenantApiKeyUsageLog
                  GROUP BY ApiKeyId
              ) u ON u.ApiKeyId = k.id
              WHERE k.username=@u
              ORDER BY
                CASE WHEN k.ExpiresAt IS NULL OR k.ExpiresAt > GETUTCDATE() THEN 0 ELSE 1 END,
                k.createdat DESC", connection);
        cmd.Parameters.AddWithValue("@u", userName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ApiKeyRecordDto
            {
                Id = Convert.ToInt32(reader["id"]),
                ApiKey = reader["apikey"]?.ToString() ?? "",
                TenantId = reader["tenantId"]?.ToString() ?? "",
                KeyLabel = reader["KeyLabel"] == DBNull.Value ? null : reader["KeyLabel"]?.ToString(),
                IsEnabled = reader["IsEnabled"] != DBNull.Value && Convert.ToBoolean(reader["IsEnabled"]),
                ExpiresAt = reader["ExpiresAt"] == DBNull.Value ? null : Convert.ToDateTime(reader["ExpiresAt"]),
                CreatedAt = Convert.ToDateTime(reader["createdat"]),
                TotalCalls = Convert.ToInt32(reader["TotalCalls"])
            });
        }

        return list;
    }

    public async Task<bool> SetApiKeyEnabledAsync(string userName, string password, int apiKeyId, bool enabled)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return false;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        if (!await UserOwnsApiKeyAsync(userName, password, apiKeyId, connection))
            return false;

        using var cmd = new SqlCommand(
            "UPDATE tenantuserApiKey SET IsEnabled=@en WHERE id=@id AND username=@u AND password=@p", connection);
        cmd.Parameters.AddWithValue("@en", enabled);
        cmd.Parameters.AddWithValue("@id", apiKeyId);
        cmd.Parameters.AddWithValue("@u", userName);
        cmd.Parameters.AddWithValue("@p", password);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> SetApiKeyEnabledByEmailAsync(string userName, int apiKeyId, bool enabled)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return false;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        using var cmd = new SqlCommand(
            "UPDATE tenantuserApiKey SET IsEnabled=@en WHERE id=@id AND username=@u", connection);
        cmd.Parameters.AddWithValue("@en", enabled);
        cmd.Parameters.AddWithValue("@id", apiKeyId);
        cmd.Parameters.AddWithValue("@u", userName);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteApiKeyAsync(string userName, string password, int apiKeyId)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return false;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        if (!await UserOwnsApiKeyAsync(userName, password, apiKeyId, connection))
            return false;

        return await DeleteApiKeyCoreAsync(apiKeyId, connection);
    }

    public async Task<bool> DeleteApiKeyByEmailAsync(string userName, int apiKeyId)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return false;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        using var check = new SqlCommand(
            "SELECT COUNT(1) FROM tenantuserApiKey WHERE id=@id AND username=@u", connection);
        check.Parameters.AddWithValue("@id", apiKeyId);
        check.Parameters.AddWithValue("@u", userName);
        if (Convert.ToInt32(await check.ExecuteScalarAsync() ?? 0) == 0)
            return false;

        return await DeleteApiKeyCoreAsync(apiKeyId, connection);
    }

    private static async Task<bool> DeleteApiKeyCoreAsync(int apiKeyId, SqlConnection connection)
    {
        using (var delLogs = new SqlCommand("DELETE FROM tenantApiKeyUsageLog WHERE ApiKeyId=@id", connection))
        {
            delLogs.Parameters.AddWithValue("@id", apiKeyId);
            await delLogs.ExecuteNonQueryAsync();
        }

        using var del = new SqlCommand("DELETE FROM tenantuserApiKey WHERE id=@id", connection);
        del.Parameters.AddWithValue("@id", apiKeyId);
        return await del.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(bool Valid, string? Message, int ApiKeyId)> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API key is empty.", 0);

        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs))
            return (false, "Database not configured.", 0);

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();

            using var cmd = new SqlCommand(
                @"SELECT TOP 1 id, IsEnabled, ExpiresAt
                  FROM tenantuserApiKey
                  WHERE apikey=@k", connection);
            cmd.Parameters.AddWithValue("@k", apiKey);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, "Invalid API Key.", 0);

            var id = Convert.ToInt32(reader["id"]);
            var enabled = reader["IsEnabled"] == DBNull.Value || Convert.ToBoolean(reader["IsEnabled"]);
            if (!enabled)
                return (false, "API Key is disabled.", 0);

            if (reader["ExpiresAt"] != DBNull.Value)
            {
                var exp = Convert.ToDateTime(reader["ExpiresAt"]);
                if (exp < DateTime.UtcNow)
                    return (false, "API Key has expired.", 0);
            }

            return (true, null, id);
        }
        catch (SqlException ex) when (ex.Message.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
        {
            // Fallback before migration: key exists only
            return await ValidateApiKeyLegacyAsync(apiKey);
        }
    }

    private async Task<(bool Valid, string? Message, int ApiKeyId)> ValidateApiKeyLegacyAsync(string apiKey)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return (false, "Database not configured.", 0);

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        using var cmd = new SqlCommand(
            "SELECT TOP 1 id FROM tenantuserApiKey WHERE apikey=@k", connection);
        cmd.Parameters.AddWithValue("@k", apiKey);
        var result = await cmd.ExecuteScalarAsync();
        if (result == null) return (false, "Invalid API Key.", 0);
        return (true, null, Convert.ToInt32(result));
    }

    public async Task LogUsageAsync(int apiKeyId, string apiKey, string path, string method, int statusCode, int latencyMs, string? clientIp)
    {
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return;

        var functionName = MapPathToFunctionName(path);
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();

            using var cmd = new SqlCommand(
                @"INSERT INTO tenantApiKeyUsageLog
                  (ApiKeyId, ApiKey, FunctionName, Endpoint, HttpMethod, StatusCode, LatencyMs, ClientIp, CalledAt)
                  VALUES (@kid, @k, @fn, @ep, @m, @st, @lat, @ip, GETUTCDATE())", connection);
            cmd.Parameters.AddWithValue("@kid", apiKeyId);
            cmd.Parameters.AddWithValue("@k", apiKey);
            cmd.Parameters.AddWithValue("@fn", functionName);
            cmd.Parameters.AddWithValue("@ep", path.Length > 512 ? path[..512] : path);
            cmd.Parameters.AddWithValue("@m", method);
            cmd.Parameters.AddWithValue("@st", statusCode);
            cmd.Parameters.AddWithValue("@lat", latencyMs);
            cmd.Parameters.AddWithValue("@ip", (object?)clientIp ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Could not log API usage (run Scripts/ApiKeyManagement.sql if table is missing).");
        }
    }

    public async Task<IReadOnlyList<ApiUsageLogDto>> GetUsageLogsAsync(
        string userName, string password, int? apiKeyId, int days)
    {
        var list = new List<ApiUsageLogDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.Id, l.ApiKeyId, l.FunctionName, l.Endpoint, l.HttpMethod,
                           l.StatusCode, l.LatencyMs, l.ClientIp, l.CalledAt
                    FROM tenantApiKeyUsageLog l
                    INNER JOIN tenantuserApiKey k ON k.id = l.ApiKeyId
                    WHERE k.username=@u AND k.password=@p";

        if (apiKeyId.HasValue)
            sql += " AND l.ApiKeyId=@kid";
        if (days > 0)
            sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";

        sql += " ORDER BY l.CalledAt DESC";

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@u", userName);
        cmd.Parameters.AddWithValue("@p", password);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageLogDto
                {
                    Id = Convert.ToInt64(reader["Id"]),
                    ApiKeyId = Convert.ToInt32(reader["ApiKeyId"]),
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    Endpoint = reader["Endpoint"]?.ToString() ?? "",
                    HttpMethod = reader["HttpMethod"]?.ToString() ?? "",
                    StatusCode = Convert.ToInt32(reader["StatusCode"]),
                    LatencyMs = Convert.ToInt32(reader["LatencyMs"]),
                    ClientIp = reader["ClientIp"] == DBNull.Value ? null : reader["ClientIp"]?.ToString(),
                    CalledAt = Convert.ToDateTime(reader["CalledAt"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage log query failed.");
        }

        return list;
    }

    public async Task<IReadOnlyList<ApiUsageLogDto>> GetUsageLogsByEmailAsync(
        string userName, int? apiKeyId, int days)
    {
        var list = new List<ApiUsageLogDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.Id, l.ApiKeyId, l.FunctionName, l.Endpoint, l.HttpMethod,
                           l.StatusCode, l.LatencyMs, l.ClientIp, l.CalledAt
                    FROM tenantApiKeyUsageLog l
                    INNER JOIN tenantuserApiKey k ON k.id = l.ApiKeyId
                    WHERE k.username=@u";

        if (apiKeyId.HasValue)
            sql += " AND l.ApiKeyId=@kid";
        if (days > 0)
            sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";

        sql += " ORDER BY l.CalledAt DESC";

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@u", userName);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageLogDto
                {
                    Id = Convert.ToInt64(reader["Id"]),
                    ApiKeyId = Convert.ToInt32(reader["ApiKeyId"]),
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    Endpoint = reader["Endpoint"]?.ToString() ?? "",
                    HttpMethod = reader["HttpMethod"]?.ToString() ?? "",
                    StatusCode = Convert.ToInt32(reader["StatusCode"]),
                    LatencyMs = Convert.ToInt32(reader["LatencyMs"]),
                    ClientIp = reader["ClientIp"] == DBNull.Value ? null : reader["ClientIp"]?.ToString(),
                    CalledAt = Convert.ToDateTime(reader["CalledAt"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage log query failed.");
        }

        return list;
    }

    /// <summary>All usage logs (playground dashboard) — optional email filter via GetUsageLogsByEmailAsync.</summary>
    public async Task<IReadOnlyList<ApiUsageLogDto>> GetAllUsageLogsAsync(int? apiKeyId, int days)
    {
        var list = new List<ApiUsageLogDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.Id, l.ApiKeyId, l.FunctionName, l.Endpoint, l.HttpMethod,
                           l.StatusCode, l.LatencyMs, l.ClientIp, l.CalledAt
                    FROM tenantApiKeyUsageLog l
                    WHERE 1=1";

        if (apiKeyId.HasValue)
            sql += " AND l.ApiKeyId=@kid";
        if (days > 0)
            sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";

        sql += " ORDER BY l.CalledAt DESC";

        using var cmd = new SqlCommand(sql, connection);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageLogDto
                {
                    Id = Convert.ToInt64(reader["Id"]),
                    ApiKeyId = Convert.ToInt32(reader["ApiKeyId"]),
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    Endpoint = reader["Endpoint"]?.ToString() ?? "",
                    HttpMethod = reader["HttpMethod"]?.ToString() ?? "",
                    StatusCode = Convert.ToInt32(reader["StatusCode"]),
                    LatencyMs = Convert.ToInt32(reader["LatencyMs"]),
                    ClientIp = reader["ClientIp"] == DBNull.Value ? null : reader["ClientIp"]?.ToString(),
                    CalledAt = Convert.ToDateTime(reader["CalledAt"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage log query failed.");
        }

        return list;
    }

    public async Task<IReadOnlyList<ApiUsageSummaryDto>> GetAllUsageSummaryByFunctionAsync(int? apiKeyId, int days)
    {
        var list = new List<ApiUsageSummaryDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.FunctionName, COUNT(*) AS CallCount
                    FROM tenantApiKeyUsageLog l
                    WHERE 1=1";

        if (apiKeyId.HasValue) sql += " AND l.ApiKeyId=@kid";
        if (days > 0) sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";
        sql += " GROUP BY l.FunctionName ORDER BY CallCount DESC";

        using var cmd = new SqlCommand(sql, connection);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageSummaryDto
                {
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    CallCount = Convert.ToInt32(reader["CallCount"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage summary query failed.");
        }

        return list;
    }

    public async Task<IReadOnlyList<ApiUsageSummaryDto>> GetUsageSummaryByFunctionAsync(
        string userName, string password, int? apiKeyId, int days)
    {
        var list = new List<ApiUsageSummaryDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.FunctionName, COUNT(*) AS CallCount
                    FROM tenantApiKeyUsageLog l
                    INNER JOIN tenantuserApiKey k ON k.id = l.ApiKeyId
                    WHERE k.username=@u AND k.password=@p";

        if (apiKeyId.HasValue) sql += " AND l.ApiKeyId=@kid";
        if (days > 0) sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";
        sql += " GROUP BY l.FunctionName ORDER BY CallCount DESC";

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@u", userName);
        cmd.Parameters.AddWithValue("@p", password);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageSummaryDto
                {
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    CallCount = Convert.ToInt32(reader["CallCount"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage summary query failed.");
        }

        return list;
    }

    public async Task<IReadOnlyList<ApiUsageSummaryDto>> GetUsageSummaryByFunctionByEmailAsync(
        string userName, int? apiKeyId, int days)
    {
        var list = new List<ApiUsageSummaryDto>();
        var cs = GetConnectionString();
        if (string.IsNullOrEmpty(cs)) return list;

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        var sql = @"SELECT l.FunctionName, COUNT(*) AS CallCount
                    FROM tenantApiKeyUsageLog l
                    INNER JOIN tenantuserApiKey k ON k.id = l.ApiKeyId
                    WHERE k.username=@u";

        if (apiKeyId.HasValue) sql += " AND l.ApiKeyId=@kid";
        if (days > 0) sql += " AND l.CalledAt >= DATEADD(day, -@days, GETUTCDATE())";
        sql += " GROUP BY l.FunctionName ORDER BY CallCount DESC";

        using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@u", userName);
        if (apiKeyId.HasValue) cmd.Parameters.AddWithValue("@kid", apiKeyId.Value);
        if (days > 0) cmd.Parameters.AddWithValue("@days", days);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ApiUsageSummaryDto
                {
                    FunctionName = reader["FunctionName"]?.ToString() ?? "",
                    CallCount = Convert.ToInt32(reader["CallCount"])
                });
            }
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Usage summary query failed.");
        }

        return list;
    }

    public static string MapPathToFunctionName(string path)
    {
        var p = path.Split('?', 2)[0].ToLowerInvariant();
        if (p.Contains("/formdetails")) return "Form Details";
        if (p.Contains("/subformdetails")) return "SubForm Details";
        if (p.Contains("/subformfields")) return "SubForm Fields";
        if (p.Contains("/subformsubmitandarchive")) return "Generate PDF";
        if (p.Contains("/getdatafromsalesforce")) return "Get Data From Salesforce";
        if (p.Contains("/access2pay/initiateprocess") || p.Contains("/access2pay/processinitiate")) return "InitiateProcess";
        if (p.Contains("/access2pay/getprocesstickets") || p.Contains("/access2pay/get")) return "GetProcessTickets";
        if (p.Contains("/access2pay/routeprocessticket") || p.Contains("/access2pay/update")) return "RouteProcessTicket";
        if (p.Contains("/access2pay/connectorinsert")) return "Access2Pay Connector Insert";
        if (p.Contains("/invoiceocr/process")) return "Invoice OCR Process";
        if (p.Contains("/invoiceocr/insert")) return "Invoice OCR Insert";
        if (p.Contains("/invoiceocr/get")) return "Invoice OCR Get";
        if (p.Contains("/invoiceocr/update")) return "Invoice OCR Update";
        if (p.Contains("/filesummary")) return "File Summary";
        if (p.Contains("/kycagent")) return "KYC Agent";
        if (p.Contains("/qrcode")) return "Generate QR Code";
        if (p.Contains("/client/apikey")) return "Generate API Key";
        return path.Trim('/');
    }
}
