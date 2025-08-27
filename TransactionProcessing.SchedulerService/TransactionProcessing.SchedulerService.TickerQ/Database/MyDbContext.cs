using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Configurations;

namespace TransactionProcessing.SchedulerService.TickerQ.Database
{
    public class SchedulerContext : DbContext
    {
        public SchedulerContext(DbContextOptions<SchedulerContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Apply TickerQ entity configurations explicitly
            builder.ApplyConfiguration(new TimeTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerConfigurations());
            builder.ApplyConfiguration(new CronTickerOccurrenceConfigurations());

            // Alternatively, apply all configurations from assembly:
            // builder.ApplyConfigurationsFromAssembly(typeof(TimeTickerConfigurations).Assembly);
        }
    }
}
