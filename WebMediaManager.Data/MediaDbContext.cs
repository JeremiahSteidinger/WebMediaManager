using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Settings;

namespace WebMediaManager.Data;

/// <summary>
/// EF Core context for the media library. Resolve it through <see cref="IDbContextFactory{TContext}"/>
/// (registered by <see cref="DependencyInjection.AddMediaData"/>) and use a short-lived instance per
/// operation — Blazor Server circuits are long-lived and concurrent, so a shared/scoped context throws.
/// </summary>
public class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options)
{
    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<AppSettings> Settings => Set<AppSettings>();

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<TvShow> TvShows => Set<TvShow>();

    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Episode> Episodes => Set<Episode>();

    public DbSet<Artwork> Artworks => Set<Artwork>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
