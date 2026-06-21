using System.Text.Json.Serialization;

namespace WebMediaManager.Providers.Imdb;

// Internal OMDb JSON shapes. OMDb uses PascalCase keys and returns the literal string "N/A" for
// missing values, so most fields are strings the provider parses/cleans. "Response" is "True"/"False"
// and "Error" carries a message when a lookup misses.

internal sealed class OmdbSearchResponse
{
    [JsonPropertyName("Search")] public List<OmdbSearchItem>? Search { get; set; }
    [JsonPropertyName("Response")] public string? Response { get; set; }
    [JsonPropertyName("Error")] public string? Error { get; set; }
}

internal sealed class OmdbSearchItem
{
    [JsonPropertyName("Title")] public string? Title { get; set; }
    [JsonPropertyName("Year")] public string? Year { get; set; }
    [JsonPropertyName("imdbID")] public string? ImdbId { get; set; }
    [JsonPropertyName("Type")] public string? Type { get; set; }
    [JsonPropertyName("Poster")] public string? Poster { get; set; }
}

internal sealed class OmdbTitle
{
    [JsonPropertyName("Title")] public string? Title { get; set; }
    [JsonPropertyName("Year")] public string? Year { get; set; }
    [JsonPropertyName("Released")] public string? Released { get; set; }
    [JsonPropertyName("Runtime")] public string? Runtime { get; set; }
    [JsonPropertyName("Genre")] public string? Genre { get; set; }
    [JsonPropertyName("Plot")] public string? Plot { get; set; }
    [JsonPropertyName("Production")] public string? Production { get; set; }
    [JsonPropertyName("Poster")] public string? Poster { get; set; }
    [JsonPropertyName("imdbRating")] public string? ImdbRating { get; set; }
    [JsonPropertyName("imdbVotes")] public string? ImdbVotes { get; set; }
    [JsonPropertyName("imdbID")] public string? ImdbId { get; set; }
    [JsonPropertyName("Type")] public string? Type { get; set; }
    [JsonPropertyName("Response")] public string? Response { get; set; }
    [JsonPropertyName("Error")] public string? Error { get; set; }
}

internal sealed class OmdbSeasonResponse
{
    [JsonPropertyName("Episodes")] public List<OmdbEpisode>? Episodes { get; set; }
    [JsonPropertyName("Response")] public string? Response { get; set; }
    [JsonPropertyName("Error")] public string? Error { get; set; }
}

internal sealed class OmdbEpisode
{
    [JsonPropertyName("Title")] public string? Title { get; set; }
    [JsonPropertyName("Released")] public string? Released { get; set; }
    [JsonPropertyName("Episode")] public string? Episode { get; set; }
    [JsonPropertyName("imdbID")] public string? ImdbId { get; set; }
}
