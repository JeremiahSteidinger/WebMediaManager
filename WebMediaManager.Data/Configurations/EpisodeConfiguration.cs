using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("Episodes");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(500);
        builder.Property(e => e.VideoFilePath).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.TmdbId).HasMaxLength(64);
        builder.Property(e => e.TvdbId).HasMaxLength(64);

        builder.HasIndex(e => e.SeasonId);
    }
}
