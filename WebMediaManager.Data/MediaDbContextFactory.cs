using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebMediaManager.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the model without booting the web host.
/// The connection string here is only used by design-time tooling (e.g. <c>database update</c>);
/// the running app supplies its own via <see cref="DependencyInjection.AddMediaData"/>.
/// </summary>
public sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseSqlite("Data Source=wmm-design.sqlite")
            .Options;

        return new MediaDbContext(options);
    }
}
