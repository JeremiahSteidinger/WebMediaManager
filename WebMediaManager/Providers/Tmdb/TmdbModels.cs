using System.Text.Json.Serialization;

namespace WebMediaManager.Providers.Tmdb;

// Internal TMDB JSON shapes. Explicit property names keep deserialization independent of naming policy.

internal sealed class TmdbSearchResponse<T>
{
    [JsonPropertyName("results")] public List<T>? Results { get; set; }
}

internal sealed class TmdbMovieSearchItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
}

internal sealed class TmdbTvSearchItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
}

internal sealed class TmdbNamed
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class TmdbSeasonResponse
{
    [JsonPropertyName("episodes")] public List<TmdbEpisode>? Episodes { get; set; }
}

internal sealed class TmdbEpisode
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("season_number")] public int SeasonNumber { get; set; }
    [JsonPropertyName("episode_number")] public int EpisodeNumber { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("air_date")] public string? AirDate { get; set; }
}

// tv/{id}/episode_groups — the list of available orderings (summaries only).
internal sealed class TmdbEpisodeGroupsResponse
{
    [JsonPropertyName("results")] public List<TmdbEpisodeGroupSummary>? Results { get; set; }
}

internal sealed class TmdbEpisodeGroupSummary
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("episode_count")] public int EpisodeCount { get; set; }
    [JsonPropertyName("group_count")] public int GroupCount { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
}

// tv/episode_group/{id} — the full ordering: a list of groups (each a "season"), each holding episodes
// in order. The episodes carry their original season/episode numbers, which we re-number to the ordering.
internal sealed class TmdbEpisodeGroupDetails
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("groups")] public List<TmdbEpisodeGroupItem>? Groups { get; set; }
}

internal sealed class TmdbEpisodeGroupItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("episodes")] public List<TmdbEpisodeGroupEpisode>? Episodes { get; set; }
}

// A group's episode: the standard episode fields plus an `order` giving its position within the group.
internal sealed class TmdbEpisodeGroupEpisode
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("air_date")] public string? AirDate { get; set; }
}

internal sealed class TmdbExternalIds
{
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }
    [JsonPropertyName("tvdb_id")] public long? TvdbId { get; set; }
}

internal sealed class TmdbMovieDetails
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("original_title")] public string? OriginalTitle { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("tagline")] public string? Tagline { get; set; }
    [JsonPropertyName("runtime")] public int? Runtime { get; set; }
    [JsonPropertyName("vote_average")] public double? VoteAverage { get; set; }
    [JsonPropertyName("vote_count")] public int? VoteCount { get; set; }
    [JsonPropertyName("genres")] public List<TmdbNamed>? Genres { get; set; }
    [JsonPropertyName("production_companies")] public List<TmdbNamed>? ProductionCompanies { get; set; }
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
}

internal sealed class TmdbTvDetails
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("original_name")] public string? OriginalName { get; set; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; set; }
    [JsonPropertyName("overview")] public string? Overview { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("vote_average")] public double? VoteAverage { get; set; }
    [JsonPropertyName("vote_count")] public int? VoteCount { get; set; }
    [JsonPropertyName("genres")] public List<TmdbNamed>? Genres { get; set; }
    [JsonPropertyName("networks")] public List<TmdbNamed>? Networks { get; set; }
    [JsonPropertyName("external_ids")] public TmdbExternalIds? ExternalIds { get; set; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
}
