namespace WebMediaManager.Core.Providers;

/// <summary>
/// A provider problem the UI should surface to the user (missing/invalid API key, auth failure,
/// unexpected response). Thrown by provider implementations.
/// </summary>
public sealed class MetadataException(string message, Exception? inner = null) : Exception(message, inner);
