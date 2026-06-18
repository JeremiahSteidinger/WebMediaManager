using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Data.Configurations;

public sealed class LibraryConfiguration : IEntityTypeConfiguration<Library>
{
    public void Configure(EntityTypeBuilder<Library> builder)
    {
        builder.ToTable("Libraries");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.RootPath).IsRequired().HasMaxLength(1024);

        // Store the enum as text so the DB stays readable and survives reordering of enum members.
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(l => l.Name);
    }
}
