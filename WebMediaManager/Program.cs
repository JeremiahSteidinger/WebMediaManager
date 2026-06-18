using Microsoft.EntityFrameworkCore;
using WebMediaManager.Components;
using WebMediaManager.Core.Domain;
using WebMediaManager.Core.Nfo;
using WebMediaManager.Core.Providers;
using WebMediaManager.Core.Renaming;
using WebMediaManager.Data;
using WebMediaManager.Jobs;
using WebMediaManager.Providers;
using WebMediaManager.Providers.Tmdb;
using WebMediaManager.Providers.Tvdb;
using WebMediaManager.Services;
using WebMediaManager.Services.Scanning;

namespace WebMediaManager
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Persistence. Connection string comes from configuration so Docker can point it at the
            // persistent /data volume (ConnectionStrings__Default) while local dev uses appsettings.
            var connectionString = builder.Configuration.GetConnectionString("Default")
                ?? "Data Source=wmm.sqlite";
            builder.Services.AddMediaData(connectionString);

            // Feature services wrap the context factory; components inject these, never the context.
            builder.Services.AddScoped<ILibraryService, LibraryService>();
            builder.Services.AddScoped<ISettingsService, SettingsService>();
            builder.Services.AddScoped<IMediaService, MediaService>();

            // Background scanning: a singleton queue + progress hub, one hosted worker, scoped scanner.
            builder.Services.AddSingleton<IJobQueue, JobQueue>();
            builder.Services.AddSingleton<IScanProgressService, ScanProgressService>();
            builder.Services.AddScoped<IMediaScanner, MediaScanner>();
            builder.Services.AddHostedService<BackgroundJobWorker>();

            // Metadata providers: resilient typed HTTP clients + a singleton TVDB token cache.
            builder.Services.AddHttpClient<TmdbMetadataProvider>(c =>
                    c.BaseAddress = new Uri("https://api.themoviedb.org/3/"))
                .AddStandardResilienceHandler();
            builder.Services.AddHttpClient<TvdbMetadataProvider>(c =>
                    c.BaseAddress = new Uri("https://api4.thetvdb.com/v4/"))
                .AddStandardResilienceHandler();
            builder.Services.AddHttpClient(TvdbTokenProvider.LoginClientName, c =>
                    c.BaseAddress = new Uri("https://api4.thetvdb.com/v4/"))
                .AddStandardResilienceHandler();
            builder.Services.AddSingleton<TvdbTokenProvider>();

            builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<TmdbMetadataProvider>());
            builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<TvdbMetadataProvider>());
            builder.Services.AddScoped<IMetadataProviderResolver, MetadataProviderResolver>();
            builder.Services.AddScoped<IIdentifyService, IdentifyService>();

            // Artwork downloader (resilient typed client, saves images next to media).
            builder.Services.AddHttpClient<IArtworkService, ArtworkService>()
                .AddStandardResilienceHandler();

            // Renaming engine.
            builder.Services.AddSingleton<ITokenEngine, TokenEngine>();
            builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();
            builder.Services.AddSingleton<RenamePlanner>();
            builder.Services.AddScoped<IRenameService, RenameService>();

            // NFO generation + reading.
            builder.Services.AddSingleton<INfoWriter, NfoWriter>();
            builder.Services.AddSingleton<INfoReader, NfoReader>();
            builder.Services.AddScoped<INfoFileService, NfoFileService>();

            var app = builder.Build();

            // Create/upgrade the database before serving traffic.
            await app.Services.MigrateMediaDatabaseAsync();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Streams a downloaded artwork file for an item (poster by default). 404 when not yet fetched.
            app.MapGet("/art/{itemId:guid}", async (
                Guid itemId, string? kind, IDbContextFactory<MediaDbContext> factory, CancellationToken ct) =>
            {
                var artKind = Enum.TryParse<ArtworkKind>(kind, ignoreCase: true, out var k) ? k : ArtworkKind.Poster;

                await using var db = await factory.CreateDbContextAsync(ct);
                var art = await db.Artworks.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.MediaItemId == itemId && a.Kind == artKind && a.LocalRelativePath != null, ct);
                if (art?.LocalRelativePath is null)
                {
                    return Results.NotFound();
                }

                var item = await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == itemId, ct);
                var library = item is null
                    ? null
                    : await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == item.LibraryId, ct);
                if (library is null)
                {
                    return Results.NotFound();
                }

                var fullPath = Path.Combine(library.RootPath, art.LocalRelativePath);
                if (!File.Exists(fullPath))
                {
                    return Results.NotFound();
                }

                var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg",
                };
                return Results.File(fullPath, contentType);
            });

            await app.RunAsync();
        }
    }
}
