using WebMediaManager.Core.Domain;

namespace WebMediaManager.Services;

public interface IMediaService
{
    Task<IReadOnlyList<MediaItem>> GetItemsAsync(Guid libraryId, MatchState? state = null, CancellationToken ct = default);

    /// <summary>Loads a single item; TV shows include their seasons and episodes.</summary>
    Task<MediaItem?> GetItemAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, int>> GetCountsByLibraryAsync(CancellationToken ct = default);

    Task<(int Movies, int Shows)> GetTotalsAsync(CancellationToken ct = default);
}
