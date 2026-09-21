using HealthMonitoring.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HealthMonitoring.Persistence;

public sealed class HealthMonitoringDbContext(DbContextOptions<HealthMonitoringDbContext> options) : DbContext(options)
{
    private const string BigintColumnType = "bigint";
    private const string NvarcharMaxColumnType = "nvarchar(max)";

    private static readonly ValueConverter<TimeSpan, long> TimeSpanTicksConverter = new(
        value => value.Ticks,
        value => TimeSpan.FromTicks(value));

    private static readonly ValueConverter<TimeSpan?, long?> NullableTimeSpanTicksConverter = new(
        value => value.HasValue ? value.Value.Ticks : null,
        value => value.HasValue ? TimeSpan.FromTicks(value.Value) : null);

    public DbSet<MonitoredService> MonitoredServices => Set<MonitoredService>();
    public DbSet<HealthObservation> HealthObservations => Set<HealthObservation>();
    public DbSet<HealthCheckResult> HealthCheckResults => Set<HealthCheckResult>();
    public DbSet<ServiceIncident> ServiceIncidents => Set<ServiceIncident>();
    public DbSet<ServiceStatusSnapshot> ServiceStatusSnapshots => Set<ServiceStatusSnapshot>();
    public DbSet<ServiceDependencyLink> ServiceDependencyLinks => Set<ServiceDependencyLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MonitoredService>(entity =>
        {
            entity.HasKey(service => service.Id);
            entity.HasIndex(service => service.ServiceId).IsUnique();
            entity.Property(service => service.ServiceId).HasMaxLength(200).IsRequired();
            entity.Property(service => service.Name).HasMaxLength(300).IsRequired();
            entity.Property(service => service.Environment).HasMaxLength(100).IsRequired();
            entity.Property(service => service.MonitorType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(service => service.HealthUrl).HasConversion<string>().HasMaxLength(2048).IsRequired(false);
            entity.Property(service => service.ConnectionString).HasColumnType(NvarcharMaxColumnType);
            entity.Property(service => service.StatusPolicyJson).HasColumnType(NvarcharMaxColumnType).IsRequired();
            entity.Property(service => service.PollingInterval).HasConversion(TimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Property(service => service.RequestTimeout).HasConversion(TimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Property(service => service.RetentionPeriod).HasConversion(TimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Ignore(service => service.StatusPolicy);
        });

        modelBuilder.Entity<HealthObservation>(entity =>
        {
            entity.HasKey(observation => observation.Id);
            entity.HasIndex(observation => new { observation.MonitoredServiceId, observation.ObservedAtUtc });
            entity.Property(observation => observation.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(observation => observation.AggregateStatus).HasMaxLength(50).IsRequired();
            entity.Property(observation => observation.ResponseDuration).HasConversion(TimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Property(observation => observation.RawResponseJson).HasColumnType(NvarcharMaxColumnType);
            entity.Property(observation => observation.Error).HasColumnType(NvarcharMaxColumnType);
            entity.HasMany(observation => observation.Checks)
                .WithOne()
                .HasForeignKey(checkResult => checkResult.HealthObservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HealthCheckResult>(entity =>
        {
            entity.HasKey(checkResult => checkResult.Id);
            entity.Property(checkResult => checkResult.Name).HasMaxLength(300).IsRequired();
            entity.Property(checkResult => checkResult.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(checkResult => checkResult.Duration).HasConversion(TimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Property(checkResult => checkResult.DataJson).HasColumnType(NvarcharMaxColumnType);
            entity.Property(checkResult => checkResult.Exception).HasColumnType(NvarcharMaxColumnType);
        });

        modelBuilder.Entity<ServiceIncident>(entity =>
        {
            entity.HasKey(incident => incident.Id);
            entity.HasIndex(incident => new { incident.MonitoredServiceId, incident.StartedAtUtc });
            entity.Property(incident => incident.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Ignore(incident => incident.IsOpen);
            entity.Ignore(incident => incident.Duration);
        });

        modelBuilder.Entity<ServiceStatusSnapshot>(entity =>
        {
            entity.HasKey(snapshot => snapshot.MonitoredServiceId);
            entity.Property(snapshot => snapshot.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(snapshot => snapshot.LastResponseDuration).HasConversion(NullableTimeSpanTicksConverter).HasColumnType(BigintColumnType);
            entity.Property(snapshot => snapshot.LastError).HasColumnType(NvarcharMaxColumnType);
        });

        modelBuilder.Entity<ServiceDependencyLink>(entity =>
        {
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => new { link.MonitoredServiceId, link.DependencyName }).IsUnique();
            entity.Property(link => link.DependencyName).HasMaxLength(300).IsRequired();
            entity.HasOne<MonitoredService>()
                .WithMany()
                .HasForeignKey(link => link.MonitoredServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MonitoredService>()
                .WithMany()
                .HasForeignKey(link => link.TargetMonitoredServiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
