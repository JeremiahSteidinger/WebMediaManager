using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Subject).HasMaxLength(500);
        builder.Property(a => a.Message).HasMaxLength(2000);

        // Stored as UTC ticks (INTEGER): SQLite can't ORDER BY a DateTimeOffset (TEXT) column, and the
        // activity feed is always sorted newest-first. Ticks ordering is order-preserving in time.
        builder.Property(a => a.TimestampUtc)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

        // No FK to MediaItem: ItemId is a loose reference so entries outlive the item.
        builder.HasIndex(a => a.TimestampUtc);
    }
}
