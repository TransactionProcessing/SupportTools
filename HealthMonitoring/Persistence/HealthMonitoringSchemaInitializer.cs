using Microsoft.EntityFrameworkCore;

#pragma warning disable EF1002

namespace HealthMonitoring.Persistence;

public static class HealthMonitoringSchemaInitializer
{
    private static readonly LegacyDurationMigration[] LegacyDurationMigrations =
    [
        new(
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'PollingInterval' AND system_type_id = 41)
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__PollingIntervalTicks')
                ALTER TABLE [dbo].[MonitoredServices] ADD [__PollingIntervalTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'PollingInterval' AND system_type_id = 41)
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__PollingIntervalTicks')
                UPDATE [dbo].[MonitoredServices]
                SET [__PollingIntervalTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [PollingInterval]) / 100;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'PollingInterval' AND system_type_id = 41)
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__PollingIntervalTicks')
                ALTER TABLE [dbo].[MonitoredServices] ALTER COLUMN [__PollingIntervalTicks] bigint NOT NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'PollingInterval' AND system_type_id = 41)
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__PollingIntervalTicks')
            BEGIN
                ALTER TABLE [dbo].[MonitoredServices] DROP COLUMN [PollingInterval];
                EXEC sp_rename N'dbo.MonitoredServices.__PollingIntervalTicks', N'PollingInterval', N'COLUMN';
            END;
            """) ,
        new(
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RequestTimeout' AND system_type_id = 41) AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RequestTimeoutTicks') ALTER TABLE [dbo].[MonitoredServices] ADD [__RequestTimeoutTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RequestTimeout' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RequestTimeoutTicks') UPDATE [dbo].[MonitoredServices] SET [__RequestTimeoutTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [RequestTimeout]) / 100;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RequestTimeout' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RequestTimeoutTicks') ALTER TABLE [dbo].[MonitoredServices] ALTER COLUMN [__RequestTimeoutTicks] bigint NOT NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RequestTimeout' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RequestTimeoutTicks') BEGIN ALTER TABLE [dbo].[MonitoredServices] DROP COLUMN [RequestTimeout]; EXEC sp_rename N'dbo.MonitoredServices.__RequestTimeoutTicks', N'RequestTimeout', N'COLUMN'; END;
            """) ,
        new(
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RetentionPeriod' AND system_type_id = 41) AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RetentionPeriodTicks') ALTER TABLE [dbo].[MonitoredServices] ADD [__RetentionPeriodTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RetentionPeriod' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RetentionPeriodTicks') UPDATE [dbo].[MonitoredServices] SET [__RetentionPeriodTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [RetentionPeriod]) / 100;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RetentionPeriod' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RetentionPeriodTicks') ALTER TABLE [dbo].[MonitoredServices] ALTER COLUMN [__RetentionPeriodTicks] bigint NOT NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[MonitoredServices]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'RetentionPeriod' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MonitoredServices]') AND name = N'__RetentionPeriodTicks') BEGIN ALTER TABLE [dbo].[MonitoredServices] DROP COLUMN [RetentionPeriod]; EXEC sp_rename N'dbo.MonitoredServices.__RetentionPeriodTicks', N'RetentionPeriod', N'COLUMN'; END;
            """) ,
        new(
            """
            IF OBJECT_ID(N'[dbo].[HealthObservations]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'ResponseDuration' AND system_type_id = 41) AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'__ResponseDurationTicks') ALTER TABLE [dbo].[HealthObservations] ADD [__ResponseDurationTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthObservations]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'ResponseDuration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'__ResponseDurationTicks') UPDATE [dbo].[HealthObservations] SET [__ResponseDurationTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [ResponseDuration]) / 100;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthObservations]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'ResponseDuration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'__ResponseDurationTicks') ALTER TABLE [dbo].[HealthObservations] ALTER COLUMN [__ResponseDurationTicks] bigint NOT NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthObservations]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'ResponseDuration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthObservations]') AND name = N'__ResponseDurationTicks') BEGIN ALTER TABLE [dbo].[HealthObservations] DROP COLUMN [ResponseDuration]; EXEC sp_rename N'dbo.HealthObservations.__ResponseDurationTicks', N'ResponseDuration', N'COLUMN'; END;
            """) ,
        new(
            """
            IF OBJECT_ID(N'[dbo].[HealthCheckResults]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'Duration' AND system_type_id = 41) AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'__DurationTicks') ALTER TABLE [dbo].[HealthCheckResults] ADD [__DurationTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthCheckResults]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'Duration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'__DurationTicks') UPDATE [dbo].[HealthCheckResults] SET [__DurationTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [Duration]) / 100;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthCheckResults]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'Duration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'__DurationTicks') ALTER TABLE [dbo].[HealthCheckResults] ALTER COLUMN [__DurationTicks] bigint NOT NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[HealthCheckResults]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'Duration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[HealthCheckResults]') AND name = N'__DurationTicks') BEGIN ALTER TABLE [dbo].[HealthCheckResults] DROP COLUMN [Duration]; EXEC sp_rename N'dbo.HealthCheckResults.__DurationTicks', N'Duration', N'COLUMN'; END;
            """) ,
        new(
            """
            IF OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'LastResponseDuration' AND system_type_id = 41) AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'__LastResponseDurationTicks') ALTER TABLE [dbo].[ServiceStatusSnapshots] ADD [__LastResponseDurationTicks] bigint NULL;
            """,
            """
            IF OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'LastResponseDuration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'__LastResponseDurationTicks') UPDATE [dbo].[ServiceStatusSnapshots] SET [__LastResponseDurationTicks] = DATEDIFF_BIG(NANOSECOND, CAST('00:00:00' AS time), [LastResponseDuration]) / 100;
            """,
            null,
            """
            IF OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'LastResponseDuration' AND system_type_id = 41) AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ServiceStatusSnapshots]') AND name = N'__LastResponseDurationTicks') BEGIN ALTER TABLE [dbo].[ServiceStatusSnapshots] DROP COLUMN [LastResponseDuration]; EXEC sp_rename N'dbo.ServiceStatusSnapshots.__LastResponseDurationTicks', N'LastResponseDuration', N'COLUMN'; END;
            """)
    ];

    public static async Task InitializeAsync(HealthMonitoringDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        foreach (var migration in LegacyDurationMigrations)
            await ConvertLegacyColumnAsync(dbContext, migration, cancellationToken);

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
        LegacyDurationMigration migration,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(migration.AddTemporaryColumnSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(migration.CopyDurationSql, cancellationToken);
        if (migration.MakeRequiredSql is not null)
            await dbContext.Database.ExecuteSqlRawAsync(migration.MakeRequiredSql, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(migration.ReplaceColumnSql, cancellationToken);
    }

    private sealed record LegacyDurationMigration(
        string AddTemporaryColumnSql,
        string CopyDurationSql,
        string? MakeRequiredSql,
        string ReplaceColumnSql);
}

#pragma warning restore EF1002
