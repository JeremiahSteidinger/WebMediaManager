namespace WebMediaManager.Core.Settings;

/// <summary>
/// All app configuration in a single-row table. The nested groups are persisted as JSON columns,
/// so adding a field is a code-only change with no schema migration.
/// </summary>
public class AppSettings
{
    /// <summary>The one and only settings row.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public ProviderSettings Providers { get; set; } = new();

    public RenamePatternSettings RenamePatterns { get; set; } = new();

    public NfoSettings Nfo { get; set; } = new();

    public ArtworkSettings Artwork { get; set; } = new();
}
