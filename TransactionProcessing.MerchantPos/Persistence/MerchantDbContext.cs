using Microsoft.EntityFrameworkCore;
using MerchantPos.EF.Models;

namespace MerchantPos.EF.Persistence
{
    public class MerchantDbContext : DbContext
    {
        public MerchantDbContext(DbContextOptions<MerchantDbContext> opts) : base(opts) { }

        public DbSet<Merchant> Merchants { get; set; }
        public DbSet<OperatorTotal> OperatorTotals { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Merchant>().HasKey(m => m.MerchantId);
            modelBuilder.Entity<OperatorTotal>().HasIndex(o => new { o.MerchantId, o.OperatorId });
        }
    }
}
