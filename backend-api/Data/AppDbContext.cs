using Microsoft.EntityFrameworkCore;

namespace backend_api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<IdempotencyKey> IdempotencyKeys { get; set; }
        public DbSet<EventTracking> EventTrackings { get; set; }
        public DbSet<FailedCommand> FailedCommands { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IdempotencyKey>(entity =>
            {
                entity.HasKey(e => e.CommandId);
                entity.HasIndex(e => e.CommandId).IsUnique();
            });

            modelBuilder.Entity<EventTracking>(entity =>
            {
                entity.HasKey(e => e.EventId);
                entity.HasIndex(e => e.EventId).IsUnique();
            });

            modelBuilder.Entity<FailedCommand>(entity =>
            {
                entity.HasKey(e => e.CommandId);
                entity.HasIndex(e => e.CommandId).IsUnique();
            });
        }
    }

    public class IdempotencyKey
    {
        public string CommandId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "success", "failed"
        public string? Response { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    public class EventTracking
    {
        public string EventId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Metadata { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class FailedCommand
    {
        public string CommandId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime FailedAt { get; set; } = DateTime.UtcNow;
    }
}
