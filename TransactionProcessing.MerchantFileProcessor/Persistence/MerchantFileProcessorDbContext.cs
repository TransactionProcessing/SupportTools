using Microsoft.EntityFrameworkCore;

namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class MerchantFileProcessorDbContext(DbContextOptions<MerchantFileProcessorDbContext> options) : DbContext(options)
{
    public DbSet<FileSendRecord> FileSendRecords => this.Set<FileSendRecord>();

    public DbSet<FileSendRecordLineStatus> FileSendRecordLineStatuses => this.Set<FileSendRecordLineStatus>();

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