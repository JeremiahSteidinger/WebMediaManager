using System.Globalization;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Core.Renaming;

/// <summary>Builds the token dictionaries the <see cref="ITokenEngine"/> expands, from domain entities.</summary>
public static class RenameTokens
{
    private static Dictionary<string, string?> New() => new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string?> ForMovie(Movie m)
    {
        var t = New();
        t["title"] = m.Title;
        t["originaltitle"] = m.OriginalTitle;
        t["sorttitle"] = m.SortTitle;
        t["year"] = m.Year?.ToString(CultureInfo.InvariantCulture);
        t["tmdbid"] = m.TmdbId;
        t["imdbid"] = m.ImdbId;
        t["rating"] = m.Rating?.ToString("0.0", CultureInfo.InvariantCulture);
        return t;
    }

    public static IReadOnlyDictionary<string, string?> ForShow(TvShow s)
    {
        var t = New();
        t["showtitle"] = s.Title;
        t["title"] = s.Title;
        t["year"] = s.Year?.ToString(CultureInfo.InvariantCulture);
        t["tvdbid"] = s.TvdbId;
        t["tmdbid"] = s.TmdbId;
        t["imdbid"] = s.ImdbId;
        t["status"] = s.Status == TvShowStatus.Unknown ? null : s.Status.ToString();
        return t;
    }

    public static IReadOnlyDictionary<string, string?> ForSeason(TvShow s, int seasonNumber)
    {
        var t = New();
        t["showtitle"] = s.Title;
        t["year"] = s.Year?.ToString(CultureInfo.InvariantCulture);
        t["seasonNr"] = seasonNumber.ToString(CultureInfo.InvariantCulture);
        t["seasonNr2"] = seasonNumber.ToString("00", CultureInfo.InvariantCulture);
        return t;
    }

    public static IReadOnlyDictionary<string, string?> ForEpisode(TvShow s, Episode e)
    {
        var t = New();
        t["showtitle"] = s.Title;
        t["year"] = s.Year?.ToString(CultureInfo.InvariantCulture);
        t["seasonNr"] = e.SeasonNumber.ToString(CultureInfo.InvariantCulture);
        t["seasonNr2"] = e.SeasonNumber.ToString("00", CultureInfo.InvariantCulture);
        t["episodeNr"] = e.EpisodeNumber.ToString(CultureInfo.InvariantCulture);
        t["episodeNr2"] = e.EpisodeNumber.ToString("00", CultureInfo.InvariantCulture);
        t["title"] = e.Title;
        t["airdate"] = e.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return t;
    }
}
