-- Run against the TENANT database (tenant.connectionString), NOT the main eZApi tenant catalog.
-- Playground also auto-creates this table on first InitiateProcess if missing.

IF OBJECT_ID(N'dbo.Access2PayProcessIoLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Access2PayProcessIoLog (
        Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo   NVARCHAR(100) NULL,
        FileName      NVARCHAR(512) NULL,
        SubmittedFrom NVARCHAR(256) NULL,
        InputJson     NVARCHAR(MAX) NULL,  -- file meta + OCR (uploadforStaticMetadata)
        OutputJson    NVARCHAR(MAX) NULL,  -- final InitiateProcess response payload
        IsSuccess     BIT NOT NULL CONSTRAINT DF_Access2PayProcessIoLog_IsSuccess DEFAULT (1),
        ErrorMessage  NVARCHAR(MAX) NULL,
        CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_Access2PayProcessIoLog_CreatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_Access2PayProcessIoLog_CreatedAt
        ON dbo.Access2PayProcessIoLog (CreatedAt DESC);

    CREATE INDEX IX_Access2PayProcessIoLog_ReferenceNo
        ON dbo.Access2PayProcessIoLog (ReferenceNo);
END
GO
