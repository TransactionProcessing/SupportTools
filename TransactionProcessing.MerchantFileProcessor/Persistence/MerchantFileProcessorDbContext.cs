using Microsoft.EntityFrameworkCore;

namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class MerchantFileProcessorDbContext(DbContextOptions<MerchantFileProcessorDbContext> options) : DbContext(options)
{
    public DbSet<FileSendRecord> FileSendRecords => this.Set<FileSendRecord>();

    public DbSet<FileSendRecordLineStatus> FileSendRecordLineStatuses => this.Set<FileSendRecordLineStatus>();

    public DbSet<MerchantProcessingAuthenticationRecord> MerchantProcessingAuthenticationRecords => this.Set<MerchantProcessingAuthenticationRecord>();

    public DbSet<MerchantProcessingFileProcessingRecord> MerchantProcessingFileProcessingRecords => this.Set<MerchantProcessingFileProcessingRecord>();

    public DbSet<MerchantProcessingTransactionGenerationRecord> MerchantProcessingTransactionGenerationRecords => this.Set<MerchantProcessingTransactionGenerationRecord>();

    public DbSet<MerchantProcessingFileStatusPollingRecord> MerchantProcessingFileStatusPollingRecords => this.Set<MerchantProcessingFileStatusPollingRecord>();

    public DbSet<MerchantProcessingFileProfileRecord> MerchantProcessingFileProfileRecords => this.Set<MerchantProcessingFileProfileRecord>();

    public DbSet<MerchantProcessingFileProfileFieldRecord> MerchantProcessingFileProfileFieldRecords => this.Set<MerchantProcessingFileProfileFieldRecord>();

    public DbSet<MerchantProcessingFileProfileHeaderFieldRecord> MerchantProcessingFileProfileHeaderFieldRecords => this.Set<MerchantProcessingFileProfileHeaderFieldRecord>();

    public DbSet<MerchantProcessingFileProfileTrailerFieldRecord> MerchantProcessingFileProfileTrailerFieldRecords => this.Set<MerchantProcessingFileProfileTrailerFieldRecord>();

    public DbSet<MerchantProcessingContractDefinitionRecord> MerchantProcessingContractDefinitionRecords => this.Set<MerchantProcessingContractDefinitionRecord>();

    public DbSet<MerchantProcessingMerchantRecord> MerchantProcessingMerchantRecords => this.Set<MerchantProcessingMerchantRecord>();

    public DbSet<MerchantProcessingMerchantRunTimeRecord> MerchantProcessingMerchantRunTimeRecords => this.Set<MerchantProcessingMerchantRunTimeRecord>();

    public DbSet<MerchantProcessingConfigurationRecord> MerchantProcessingConfigurationRecords => this.Set<MerchantProcessingConfigurationRecord>();

    public DbSet<MerchantRunRecord> MerchantRunRecords => this.Set<MerchantRunRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileSendRecord>(entity =>
        {
            entity.ToTable("FileSendRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.MerchantId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.EstateId).HasMaxLength(64);
            entity.Property(record => record.MerchantName).HasMaxLength(256);
            entity.Property(record => record.ContractId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.ContractName).HasMaxLength(256);
            entity.Property(record => record.FileName).HasMaxLength(260);
            entity.Property(record => record.FileProfileId).HasMaxLength(128);
            entity.Property(record => record.Format).HasMaxLength(32);
            entity.Property(record => record.FileProcessorFileId).HasMaxLength(64);
            entity.Property(record => record.ScheduledRunUtc);
            entity.Property(record => record.Status).HasMaxLength(32).IsRequired();
            entity.Property(record => record.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(record => new { record.MerchantId, record.ProcessedUtc });
            entity.HasIndex(record => new { record.MerchantId, record.ContractId, record.ScheduledRunUtc });
            entity.HasIndex(record => record.ProcessedUtc);
        });

        modelBuilder.Entity<FileSendRecordLineStatus>(entity =>
        {
            entity.ToTable("FileSendRecordLineStatuses");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.LineData).HasMaxLength(4096);
            entity.Property(record => record.ProcessingStatus).HasMaxLength(32).IsRequired();
            entity.Property(record => record.RejectionReason).HasMaxLength(2048);
            entity.HasIndex(record => new { record.FileSendRecordId, record.LineNumber }).IsUnique();
            entity.HasOne<FileSendRecord>()
                .WithMany(record => record.LineStatuses)
                .HasForeignKey(record => record.FileSendRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantProcessingAuthenticationRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingAuthenticationRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ClientId).HasMaxLength(128).IsRequired();
            entity.Property(record => record.ClientSecret).HasMaxLength(256).IsRequired();
            entity.Property(record => record.Scope).HasMaxLength(512);
            entity.Property(record => record.Audience).HasMaxLength(512);
            entity.Property(record => record.UpdatedUtc);
        });

        modelBuilder.Entity<MerchantProcessingFileProcessingRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileProcessingRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.UserId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.UpdatedUtc);
        });

        modelBuilder.Entity<MerchantProcessingTransactionGenerationRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingTransactionGenerationRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.UpdatedUtc);
        });

        modelBuilder.Entity<MerchantProcessingFileStatusPollingRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileStatusPollingRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.UpdatedUtc);
        });

        modelBuilder.Entity<MerchantProcessingFileProfileRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileProfiles");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.FileProfileId).HasMaxLength(128).IsRequired();
            entity.Property(record => record.FileProcessorFileProfileId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.Format).HasMaxLength(32).IsRequired();
            entity.Property(record => record.FileExtension).HasMaxLength(32).IsRequired();
            entity.Property(record => record.FileNamePattern).HasMaxLength(260);
            entity.Property(record => record.ContentType).HasMaxLength(128);
            entity.Property(record => record.Delimiter).HasMaxLength(8);
            entity.Property(record => record.RootPropertyName).HasMaxLength(128);
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => record.FileProfileId).IsUnique();
        });

        modelBuilder.Entity<MerchantProcessingFileProfileFieldRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileProfileFields");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Name).HasMaxLength(128).IsRequired();
            entity.Property(record => record.Source).HasMaxLength(128);
            entity.Property(record => record.Format).HasMaxLength(64);
            entity.Property(record => record.Value).HasMaxLength(256);
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => new { record.FileProfileRecordId, record.SortOrder });
        });

        modelBuilder.Entity<MerchantProcessingFileProfileHeaderFieldRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileProfileHeaderFields");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Name).HasMaxLength(128).IsRequired();
            entity.Property(record => record.Source).HasMaxLength(128);
            entity.Property(record => record.Format).HasMaxLength(64);
            entity.Property(record => record.Value).HasMaxLength(256);
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => new { record.FileProfileRecordId, record.SortOrder });
        });

        modelBuilder.Entity<MerchantProcessingFileProfileTrailerFieldRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingFileProfileTrailerFields");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Name).HasMaxLength(128).IsRequired();
            entity.Property(record => record.Source).HasMaxLength(128);
            entity.Property(record => record.Format).HasMaxLength(64);
            entity.Property(record => record.Value).HasMaxLength(256);
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => new { record.FileProfileRecordId, record.SortOrder });
        });

        modelBuilder.Entity<MerchantProcessingContractDefinitionRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingContractDefinitions");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ContractId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.FileProfileId).HasMaxLength(128).IsRequired();
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => record.ContractId).IsUnique();
            entity.HasIndex(record => record.FileProfileId);
        });

        modelBuilder.Entity<MerchantProcessingMerchantRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingMerchants");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Name).HasMaxLength(256).IsRequired();
            entity.Property(record => record.EstateId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.MerchantId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.RunAtUtc).HasMaxLength(16).IsRequired();
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => record.MerchantId).IsUnique();
        });

        modelBuilder.Entity<MerchantProcessingMerchantRunTimeRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingMerchantRunTimes");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.RunTimeUtc).HasMaxLength(16).IsRequired();
            entity.Property(record => record.UpdatedUtc);
            entity.HasIndex(record => new { record.MerchantRecordId, record.SortOrder });
            entity.HasOne<MerchantProcessingMerchantRecord>()
                .WithMany()
                .HasForeignKey(record => record.MerchantRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantProcessingConfigurationRecord>(entity =>
        {
            entity.ToTable("MerchantProcessingConfigurationRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ConfigurationJson).IsRequired();
            entity.Property(record => record.UpdatedUtc);
        });

        modelBuilder.Entity<MerchantRunRecord>(entity =>
        {
            entity.ToTable("MerchantRunRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.MerchantId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.MerchantName).HasMaxLength(256);
            entity.Property(record => record.Status).HasMaxLength(32).IsRequired();
            entity.Property(record => record.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(record => new { record.MerchantId, record.ScheduledRunUtc, record.CompletedUtc });
        });
    }
}
