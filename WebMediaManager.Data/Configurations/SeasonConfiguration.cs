using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(500);
        builder.Property(s => s.RelativePath).HasMaxLength(1024);

        builder.HasMany(s => s.Episodes)
            .WithOne()
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TvShowId, s.SeasonNumber }).IsUnique();
    }
}
