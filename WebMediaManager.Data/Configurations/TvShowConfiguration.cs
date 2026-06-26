using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class TvShowConfiguration : IEntityTypeConfiguration<TvShow>
{
    public void Configure(EntityTypeBuilder<TvShow> builder)
    {
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);

        builder.Property(s => s.EpisodeGroupId).HasMaxLength(64);
        builder.Property(s => s.EpisodeGroupName).HasMaxLength(200);

        builder.HasMany(s => s.Seasons)
            .WithOne()
            .HasForeignKey(s => s.TvShowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
