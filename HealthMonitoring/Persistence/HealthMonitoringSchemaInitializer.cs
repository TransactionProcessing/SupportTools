using Microsoft.EntityFrameworkCore;

#pragma warning disable EF1002

namespace HealthMonitoring.Persistence;

public static class HealthMonitoringSchemaInitializer
{
    private static readonly LegacyDurationColumn[] LegacyDurationColumns =
    [
        new("MonitoredServices", "PollingInterval", Nullable: false),
        new("MonitoredServices", "RequestTimeout", Nullable: false),
        new("MonitoredServices", "RetentionPeriod", Nullable: false),
        new("HealthObservations", "ResponseDuration", Nullable: false),
        new("HealthCheckResults", "Duration", Nullable: false),
        new("ServiceStatusSnapshots", "LastResponseDuration", Nullable: true)
    ];

    public static async Task InitializeAsync(HealthMonitoringDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var column in LegacyDurationColumns)
            await ConvertLegacyColumnAsync(dbContext, column, cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[dbo].[ServiceDependencyLinks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ServiceDependencyLinks]
                (
                    [Id] uniqueidentifier NOT NULL,
                    [MonitoredServiceId] uniqueidentifier NOT NULL,
                    [DependencyName] nvarchar(300) NOT NULL,
                    [TargetMonitoredServiceId] uniqueidentifier NOT NULL,
                    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
                    [UpdatedAtUtc] datetimeoffset(7) NOT NULL,
                    CONSTRAINT [PK_ServiceDependencyLinks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ServiceDependencyLinks_MonitoredServices_Parent] FOREIGN KEY ([MonitoredServiceId]) REFERENCES [dbo].[MonitoredServices] ([Id]),
                    CONSTRAINT [FK_ServiceDependencyLinks_MonitoredServices_Target] FOREIGN KEY ([TargetMonitoredServiceId]) REFERENCES [dbo].[MonitoredServices] ([Id])
                );
                CREATE UNIQUE INDEX [IX_ServiceDependencyLinks_MonitoredServiceId_DependencyName]
                    ON [dbo].[ServiceDependencyLinks] ([MonitoredServiceId], [DependencyName]);
            END;
            """, cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'MonitorType')
                ALTER TABLE [dbo].[MonitoredServices]
                    ADD [MonitorType] nvarchar(50) NOT NULL CONSTRAINT [DF_MonitoredServices_MonitorType] DEFAULT (N'HttpHealthEndpoint');
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'ConnectionString')
                ALTER TABLE [dbo].[MonitoredServices]
                    ADD [ConnectionString] nvarchar(max) NULL;
            """, cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'IgnoreCertificateErrors')
                ALTER TABLE [dbo].[MonitoredServices]
                    ADD [IgnoreCertificateErrors] bit NOT NULL CONSTRAINT [DF_MonitoredServices_IgnoreCertificateErrors] DEFAULT (0);
            """, cancellationToken);
    }

    private static async Task ConvertLegacyColumnAsync(
        HealthMonitoringDbContext dbContext,
        LegacyDurationColumn column,
        CancellationToken cancellationToken)
    {
        var table = $"[dbo].[{column.TableName}]";
        var oldColumn = $"[{column.ColumnName}]";
        var temporaryColumn = $"[__{column.ColumnName}Ticks]";

        await dbContext.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{table}', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column.ColumnName}' AND system_type_id = 41)
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'__{column.ColumnName}Ticks')
                ALTER TABLE {table} ADD {temporaryColumn} bigint NULL;
            """, cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{table}', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column.ColumnName}' AND system_type_id = 41)
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'__{column.ColumnName}Ticks')
                UPDATE {table}
                SET {temporaryColumn} = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), {oldColumn}) / 100;
            """, cancellationToken);

        if (!column.Nullable)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"""
                IF OBJECT_ID(N'{table}', N'U') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column.ColumnName}' AND system_type_id = 41)
                   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'__{column.ColumnName}Ticks')
                    ALTER TABLE {table} ALTER COLUMN {temporaryColumn} bigint NOT NULL;
                """, cancellationToken);
        }

        await dbContext.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{table}', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column.ColumnName}' AND system_type_id = 41)
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'{table}') AND name = N'__{column.ColumnName}Ticks')
            BEGIN
                ALTER TABLE {table} DROP COLUMN {oldColumn};
                EXEC sp_rename N'dbo.{column.TableName}.__{column.ColumnName}Ticks', N'{column.ColumnName}', N'COLUMN';
            END;
            """, cancellationToken);
    }

    private sealed record LegacyDurationColumn(string TableName, string ColumnName, bool Nullable);
}

#pragma warning restore EF1002
