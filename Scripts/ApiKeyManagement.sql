-- Run once against the eZApi tenant database (ConnectionStrings:eZApiTenantContext).
-- Adds multi-key support, enable/disable, expiry, and per-call usage logging.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tenantuserApiKey') AND name = N'IsEnabled')
    ALTER TABLE dbo.tenantuserApiKey ADD IsEnabled BIT NOT NULL CONSTRAINT DF_tenantuserApiKey_IsEnabled DEFAULT (1);

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tenantuserApiKey') AND name = N'ExpiresAt')
    ALTER TABLE dbo.tenantuserApiKey ADD ExpiresAt DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tenantuserApiKey') AND name = N'KeyLabel')
    ALTER TABLE dbo.tenantuserApiKey ADD KeyLabel NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'tenantApiKeyUsageLog')
BEGIN
    CREATE TABLE dbo.tenantApiKeyUsageLog (
        Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ApiKeyId    INT NOT NULL,
        ApiKey      NVARCHAR(256) NOT NULL,
        FunctionName NVARCHAR(128) NOT NULL,
        Endpoint    NVARCHAR(512) NOT NULL,
        HttpMethod  NVARCHAR(16) NOT NULL,
        StatusCode  INT NOT NULL,
        LatencyMs   INT NOT NULL,
        ClientIp    NVARCHAR(64) NULL,
        CalledAt    DATETIME NOT NULL CONSTRAINT DF_tenantApiKeyUsageLog_CalledAt DEFAULT (GETDATE())
    );
    CREATE INDEX IX_tenantApiKeyUsageLog_ApiKeyId ON dbo.tenantApiKeyUsageLog (ApiKeyId);
    CREATE INDEX IX_tenantApiKeyUsageLog_CalledAt ON dbo.tenantApiKeyUsageLog (CalledAt DESC);
END
GO
