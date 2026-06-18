using System.Text.Json.Serialization;

namespace WebMediaManager.Providers.Tvdb;

internal sealed class TvdbLoginResponse
{
    [JsonPropertyName("data")] public TvdbLoginData? Data { get; set; }
}

internal sealed class TvdbLoginData
{
    [JsonPropertyName("token")] public string? Token { get; set; }
}

internal sealed class TvdbSearchResponse
{
    [JsonPropertyName("data")] public List<TvdbSearchItem>? Data { get; set; }
}

internal sealed class TvdbSearchItem
{
    [JsonPropertyName("tvdb_id")] public string? TvdbId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("year")] public string? Year { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
}

internal sealed class TvdbSeriesResponse
{
    [JsonPropertyName("data")] public TvdbSeries? Data { get; set; }
}

internal sealed class TvdbSeries
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("year")] public string? Year { get; set; }
    [JsonPropertyName("firstAired")] public string? FirstAired { get; set; }
    [JsonPropertyName("status")] public TvdbNamed? Status { get; set; }
    [JsonPropertyName("genres")] public List<TvdbNamed>? Genres { get; set; }
}

internal sealed class TvdbNamed
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}
