using Microsoft.EntityFrameworkCore;
using WebMediaManager.Core.Domain;
using WebMediaManager.Data;

namespace WebMediaManager.Services;

/// <summary>
/// CRUD over <see cref="Library"/>. Uses a short-lived context per call from the factory, which is the
/// safe pattern under Blazor Server. These are low-frequency, user-initiated writes.
/// </summary>
public sealed class LibraryService(IDbContextFactory<MediaDbContext> factory) : ILibraryService
{
    public async Task<IReadOnlyList<Library>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Libraries.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct);
    }

    public async Task<Library?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<Library> CreateAsync(Library library, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (library.Id == Guid.Empty)
        {
            library.Id = Guid.NewGuid();
        }
        library.CreatedUtc = DateTimeOffset.UtcNow;
        db.Libraries.Add(library);
        await db.SaveChangesAsync(ct);
        return library;
    }

    public async Task UpdateAsync(Library library, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Libraries.Update(library);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Libraries.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (entity is not null)
        {
            db.Libraries.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }
}
