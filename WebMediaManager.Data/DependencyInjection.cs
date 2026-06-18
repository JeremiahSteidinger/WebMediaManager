using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WebMediaManager.Data;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the SQLite-backed <see cref="MediaDbContext"/> as a pooled-free factory.
    /// Consumers create a short-lived context per operation via <see cref="IDbContextFactory{TContext}"/>.
    /// </summary>
    public static IServiceCollection AddMediaData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<MediaDbContext>(options =>
            options
                .UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaInterceptor()));

        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations. Call once at startup. Safe for this single-user app;
    /// would need coordination if ever run from multiple replicas.
    /// </summary>
    public static async Task MigrateMediaDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MediaDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
