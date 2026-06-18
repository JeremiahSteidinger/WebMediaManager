using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class ArtworkConfiguration : IEntityTypeConfiguration<Artwork>
{
    public void Configure(EntityTypeBuilder<Artwork> builder)
    {
        builder.ToTable("Artworks");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.SourceUrl).HasMaxLength(1024);
        builder.Property(a => a.LocalRelativePath).HasMaxLength(1024);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(a => a.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.MediaItemId, a.Kind });
    }
}
