namespace QRCodeAPI.Models;

public class ApiKeyRecordDto
{
    public int Id { get; set; }
    public string ApiKey { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string? KeyLabel { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCalls { get; set; }
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}

public class ApiKeyGenerateResult
{
    public string TenantId { get; set; } = "";
    public string Token { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int ApiKeyId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsNew { get; set; }
}

public class ApiUsageLogDto
{
    public long Id { get; set; }
    public int ApiKeyId { get; set; }
    public string FunctionName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public int StatusCode { get; set; }
    public int LatencyMs { get; set; }
    public string? ClientIp { get; set; }
    public DateTime CalledAt { get; set; }
}

public class ApiUsageSummaryDto
{
    public string FunctionName { get; set; } = "";
    public int CallCount { get; set; }
}
