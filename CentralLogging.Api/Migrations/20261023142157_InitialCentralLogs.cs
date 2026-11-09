using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralLogging.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCentralLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.LogEvents', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LogEvents
                    (
                        EventId uniqueidentifier NOT NULL,
                        TimestampUtc datetimeoffset(7) NOT NULL,
                        LogDateUtc AS CONVERT(date, TimestampUtc) PERSISTED,
                        Severity nvarchar(16) NOT NULL,
                        ServiceName nvarchar(200) NOT NULL,
                        CorrelationId uniqueidentifier NULL,
                        TenantId uniqueidentifier NULL,
                        MerchantId uniqueidentifier NULL,
                        TraceId nvarchar(32) NULL,
                        SpanId nvarchar(16) NULL,
                        RequestId nvarchar(200) NULL,
                        Message nvarchar(max) NOT NULL,
                        MessageTemplate nvarchar(max) NULL,
                        Exception nvarchar(max) NULL,
                        PropertiesJson nvarchar(max) NOT NULL,
                        CONSTRAINT PK_LogEvents PRIMARY KEY CLUSTERED (EventId),
                        CONSTRAINT CK_LogEvents_PropertiesJson_IsJson CHECK (ISJSON(PropertiesJson) = 1)
                    );
                END;

                IF COL_LENGTH(N'dbo.LogEvents', N'LogDateUtc') IS NULL
                    ALTER TABLE dbo.LogEvents ADD LogDateUtc AS CONVERT(date, TimestampUtc) PERSISTED;

                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'CK_LogEvents_PropertiesJson_IsJson')
                    ALTER TABLE dbo.LogEvents ADD CONSTRAINT CK_LogEvents_PropertiesJson_IsJson CHECK (ISJSON(PropertiesJson) = 1);
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.ServiceStructuredFields', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ServiceStructuredFields
                    (
                        ServiceName nvarchar(200) NOT NULL,
                        FieldName nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                        JsonType nvarchar(16) NOT NULL,
                        FirstSeenUtc datetimeoffset(7) NOT NULL,
                        LastSeenUtc datetimeoffset(7) NOT NULL,
                        EventCount bigint NOT NULL,
                        CONSTRAINT PK_ServiceStructuredFields PRIMARY KEY CLUSTERED (ServiceName, FieldName, JsonType)
                    );
                END;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM dbo.ServiceStructuredFields)
                BEGIN
                    INSERT INTO dbo.ServiceStructuredFields (ServiceName, FieldName, JsonType, FirstSeenUtc, LastSeenUtc, EventCount)
                    SELECT events.ServiceName,
                           properties.[key] COLLATE Latin1_General_100_BIN2,
                           CASE properties.[type]
                               WHEN 0 THEN N'null'
                               WHEN 1 THEN N'string'
                               WHEN 2 THEN N'number'
                               WHEN 3 THEN N'boolean'
                               WHEN 4 THEN N'array'
                               WHEN 5 THEN N'object'
                               ELSE N'unknown'
                           END,
                           MIN(events.TimestampUtc),
                           MAX(events.TimestampUtc),
                           COUNT_BIG(*)
                    FROM dbo.LogEvents AS events
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(events.PropertiesJson) = 1 THEN events.PropertiesJson ELSE N'{}' END) AS properties
                    WHERE DATALENGTH(properties.[key]) <= 400
                    GROUP BY events.ServiceName, properties.[key] COLLATE Latin1_General_100_BIN2, properties.[type];
                END;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'IX_LogEvents_TimestampUtc')
                    CREATE INDEX IX_LogEvents_TimestampUtc ON dbo.LogEvents (TimestampUtc DESC);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'IX_LogEvents_LogDateUtc_TimestampUtc')
                    CREATE INDEX IX_LogEvents_LogDateUtc_TimestampUtc ON dbo.LogEvents (LogDateUtc, TimestampUtc DESC);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'IX_LogEvents_TenantId_TimestampUtc')
                    CREATE INDEX IX_LogEvents_TenantId_TimestampUtc ON dbo.LogEvents (TenantId, TimestampUtc DESC);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'IX_LogEvents_ServiceName_TimestampUtc')
                    CREATE INDEX IX_LogEvents_ServiceName_TimestampUtc ON dbo.LogEvents (ServiceName, TimestampUtc DESC);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LogEvents') AND name = N'IX_LogEvents_CorrelationId')
                    CREATE INDEX IX_LogEvents_CorrelationId ON dbo.LogEvents (CorrelationId) WHERE CorrelationId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This baseline adopts existing databases as well as creating new ones.
            // Leaving its schema intact avoids dropping pre-existing log data on rollback.
        }
    }
}
