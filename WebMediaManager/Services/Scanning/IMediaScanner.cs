namespace WebMediaManager.Services.Scanning;

/// <summary>Walks a library's root folder and records discovered movies / shows as unmatched items.</summary>
public interface IMediaScanner
{
    /// <param name="reimport">When true, re-reads NFO/artwork into items already in the DB (not just new ones).</param>
    Task ScanAsync(Guid libraryId, bool reimport, CancellationToken ct);
}
