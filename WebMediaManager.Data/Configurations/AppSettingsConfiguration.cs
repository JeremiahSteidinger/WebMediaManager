using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebMediaManager.Core.Settings;

namespace WebMediaManager.Data.Configurations;

public sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Id);

        // Single fixed row; never auto-generate the key.
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Each settings group is stored as a JSON column so new fields need no migration.
        builder.OwnsOne(s => s.Providers, b => b.ToJson());
        builder.OwnsOne(s => s.RenamePatterns, b => b.ToJson());
        builder.OwnsOne(s => s.Nfo, b => b.ToJson());
        builder.OwnsOne(s => s.Artwork, b => b.ToJson());
    }
}
