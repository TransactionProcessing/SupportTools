using Microsoft.EntityFrameworkCore;

namespace TransactionProcessing.MerchantPos.Persistence;

public sealed class MerchantPosSettingsDbContext : DbContext
{
    public MerchantPosSettingsDbContext(DbContextOptions<MerchantPosSettingsDbContext> options) : base(options)
    {
    }

    public DbSet<MerchantPosSettingsRecord> Settings => Set<MerchantPosSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MerchantPosSettingsRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<MerchantPosSettingsRecord>().Property(x => x.Json).IsRequired();
        modelBuilder.Entity<MerchantPosSettingsRecord>().Property(x => x.UpdatedUtc).IsRequired();
    }
}

public sealed class MerchantPosSettingsRecord
{
    public int Id { get; set; }
    public string Json { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
}
