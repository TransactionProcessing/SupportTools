using CentralLogging.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralLogging.Api.Data;

public sealed class CentralLogsDbContext(DbContextOptions<CentralLogsDbContext> options) : DbContext(options)
{
    public DbSet<CentralLogEvent> LogEvents => this.Set<CentralLogEvent>();
    public DbSet<ServiceStructuredField> ServiceStructuredFields => this.Set<ServiceStructuredField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<CentralLogEvent> entity = modelBuilder.Entity<CentralLogEvent>();
        entity.ToTable("LogEvents", "dbo", table =>
            table.HasCheckConstraint("CK_LogEvents_PropertiesJson_IsJson", "ISJSON([PropertiesJson]) = 1"));
        entity.HasKey(log => log.EventId);
        entity.Property(log => log.TimestampUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        entity.Property(log => log.LogDateUtc)
            .HasColumnType("date")
            .HasComputedColumnSql("CONVERT(date, [TimestampUtc])", stored: true);
        entity.Property(log => log.Severity).HasMaxLength(16).IsRequired();
        entity.Property(log => log.ServiceName).HasMaxLength(200).IsRequired();
        entity.Property(log => log.TraceId).HasMaxLength(32);
        entity.Property(log => log.SpanId).HasMaxLength(16);
        entity.Property(log => log.RequestId).HasMaxLength(200);
        entity.Property(log => log.Message).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(log => log.MessageTemplate).HasColumnType("nvarchar(max)");
        entity.Property(log => log.Exception).HasColumnType("nvarchar(max)");
        entity.Property(log => log.PropertiesJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.HasIndex(log => log.TimestampUtc).IsDescending();
        entity.HasIndex(log => new { log.LogDateUtc, log.TimestampUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_LogEvents_LogDateUtc_TimestampUtc");
        entity.HasIndex(log => new { log.TenantId, log.TimestampUtc }).IsDescending(false, true);
        entity.HasIndex(log => new { log.ServiceName, log.TimestampUtc }).IsDescending(false, true);
        entity.HasIndex(log => log.CorrelationId).HasFilter("[CorrelationId] IS NOT NULL");

        EntityTypeBuilder<ServiceStructuredField> structuredField = modelBuilder.Entity<ServiceStructuredField>();
        structuredField.ToTable("ServiceStructuredFields", "dbo");
        structuredField.HasKey(field => new { field.ServiceName, field.FieldName, field.JsonType });
        structuredField.Property(field => field.ServiceName)
            .HasMaxLength(200)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired()
            .Metadata.SetValueComparer(new ValueComparer<String>(
                (left, right) => StringComparer.Ordinal.Equals(left, right),
                value => StringComparer.Ordinal.GetHashCode(value),
                value => value));
        structuredField.Property(field => field.FieldName).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2").IsRequired();
        structuredField.Property(field => field.JsonType).HasMaxLength(16).IsRequired();
        structuredField.Property(field => field.FirstSeenUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        structuredField.Property(field => field.LastSeenUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        structuredField.Property(field => field.EventCount).IsRequired();
    }
}
