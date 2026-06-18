using WebMediaManager.Core.Domain;

namespace WebMediaManager.Services;

public interface ILibraryService
{
    Task<IReadOnlyList<Library>> GetAllAsync(CancellationToken ct = default);

    Task<Library?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Library> CreateAsync(Library library, CancellationToken ct = default);

    Task UpdateAsync(Library library, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
