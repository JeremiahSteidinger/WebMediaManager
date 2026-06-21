namespace WebMediaManager.Core.Domain;

/// <summary>The kind of action an <see cref="ActivityLog"/> entry records.</summary>
public enum ActivityCategory
{
    Identify,
    Rename,
    Artwork,
    Nfo,
    Scan,
}

/// <summary>The outcome of a logged action.</summary>
public enum ActivityStatus
{
    Success,
    Warning,
    Failure,
}

/// <summary>
/// One recorded action the app took (identify, rename, artwork, NFO, scan) and its outcome.
/// <see cref="Subject"/> is denormalized (a copy of the item/library name) so entries stay readable
/// even after the underlying item is renamed or removed; <see cref="ItemId"/> is a loose, nullable
/// back-reference with no foreign key for the same reason.
/// </summary>
public class ActivityLog
{
    public Guid Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public ActivityCategory Category { get; set; }

    public ActivityStatus Status { get; set; }

    /// <summary>Optional link back to the media item this action concerned (null for library-wide actions like scans).</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Display name of the thing acted on — the item title, or the library name for a scan.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Human-readable detail, e.g. "Matched to Tmdb #27205" or an error message.</summary>
    public string Message { get; set; } = string.Empty;
}
