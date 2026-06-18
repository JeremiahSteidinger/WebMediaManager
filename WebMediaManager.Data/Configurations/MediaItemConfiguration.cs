using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.ToTable("MediaItems");
        builder.HasKey(m => m.Id);

        // Table-per-hierarchy: Movie and TvShow share this table, distinguished by ItemType.
        builder.HasDiscriminator<string>("ItemType")
            .HasValue<Movie>("Movie")
            .HasValue<TvShow>("TvShow");

        builder.Property(m => m.Title).IsRequired().HasMaxLength(500);
        builder.Property(m => m.OriginalTitle).HasMaxLength(500);
        builder.Property(m => m.SortTitle).HasMaxLength(500);
        builder.Property(m => m.RelativePath).IsRequired().HasMaxLength(1024);

        builder.Property(m => m.MatchState).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.PrimaryProvider).HasConversion<string>().HasMaxLength(32);

        builder.Property(m => m.TmdbId).HasMaxLength(64);
        builder.Property(m => m.TvdbId).HasMaxLength(64);
        builder.Property(m => m.ImdbId).HasMaxLength(64);

        // Items belong to a library; deleting the library removes its inventory (files untouched).
        builder.HasOne<Library>()
            .WithMany()
            .HasForeignKey(m => m.LibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.LibraryId);
        builder.HasIndex(m => new { m.LibraryId, m.MatchState });
    }
}
