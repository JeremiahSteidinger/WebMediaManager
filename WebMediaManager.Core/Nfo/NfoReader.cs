using System.Globalization;
using System.Xml.Linq;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Core.Nfo;

public interface INfoReader
{
    void ReadMovie(XDocument doc, Movie movie);

    void ReadTvShow(XDocument doc, TvShow show);

    void ReadEpisode(XDocument doc, Episode episode);
}

/// <summary>
/// Parses Kodi-format NFO XML into domain fields (the inverse of <see cref="NfoWriter"/>). Only overwrites
/// a field when the NFO actually carries a value, and reads IDs from both modern <c>&lt;uniqueid&gt;</c>
/// elements and the legacy <c>&lt;tmdbid&gt;/&lt;imdbid&gt;/&lt;tvdbid&gt;/&lt;id&gt;</c> forms. Pure: no IO.
/// </summary>
public sealed class NfoReader : INfoReader
{
    public void ReadMovie(XDocument doc, Movie m)
    {
        var r = doc.Root;
        if (r is null)
        {
            return;
        }

        m.Title = Str(r, "title") ?? m.Title;
        m.OriginalTitle = Str(r, "originaltitle") ?? m.OriginalTitle;
        m.SortTitle = Str(r, "sorttitle") ?? m.SortTitle;
        m.Year = Int(r, "year") ?? YearOf(Str(r, "premiered")) ?? m.Year;
        m.Plot = Str(r, "plot") ?? m.Plot;
        m.Tagline = Str(r, "tagline") ?? m.Tagline;
        m.Runtime = Int(r, "runtime") ?? m.Runtime;
        m.Rating = Dbl(r, "rating") ?? m.Rating;
        m.ReleaseDate = DateOf(Str(r, "premiered")) ?? m.ReleaseDate;

        var genres = Repeated(r, "genre");
        if (genres.Count > 0)
        {
            m.Genres = genres;
        }
        var studios = Repeated(r, "studio");
        if (studios.Count > 0)
        {
            m.Studios = studios;
        }

        m.TmdbId = Tmdb(r) ?? m.TmdbId;
        m.ImdbId = Imdb(r) ?? m.ImdbId;
        m.TvdbId = Tvdb(r) ?? m.TvdbId;
    }

    public void ReadTvShow(XDocument doc, TvShow s)
    {
        var r = doc.Root;
        if (r is null)
        {
            return;
        }

        s.Title = Str(r, "title") ?? s.Title;
        s.OriginalTitle = Str(r, "originaltitle") ?? s.OriginalTitle;
        s.Year = Int(r, "year") ?? YearOf(Str(r, "premiered")) ?? s.Year;
        s.Plot = Str(r, "plot") ?? s.Plot;
        s.Rating = Dbl(r, "rating") ?? s.Rating;
        s.Status = ParseStatus(Str(r, "status")) ?? s.Status;

        var genres = Repeated(r, "genre");
        if (genres.Count > 0)
        {
            s.Genres = genres;
        }
        var studios = Repeated(r, "studio");
        if (studios.Count > 0)
        {
            s.Studios = studios;
        }

        s.TmdbId = Tmdb(r) ?? s.TmdbId;
        s.ImdbId = Imdb(r) ?? s.ImdbId;
        s.TvdbId = Tvdb(r) ?? s.TvdbId;
    }

    public void ReadEpisode(XDocument doc, Episode e)
    {
        var r = doc.Root;
        if (r is null)
        {
            return;
        }

        e.Title = Str(r, "title") ?? e.Title;
        e.Plot = Str(r, "plot") ?? e.Plot;
        e.AirDate = DateOf(Str(r, "aired")) ?? e.AirDate;
        e.Rating = Dbl(r, "rating") ?? e.Rating;
        e.TmdbId = Tmdb(r) ?? e.TmdbId;
        e.TvdbId = Tvdb(r) ?? e.TvdbId;
    }

    private static string? Str(XElement parent, string name)
    {
        var value = parent.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? Int(XElement parent, string name) =>
        int.TryParse(Str(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Dbl(XElement parent, string name) =>
        double.TryParse(Str(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static List<string> Repeated(XElement parent, string name) =>
        parent.Elements(name).Select(e => e.Value.Trim()).Where(s => s.Length > 0).ToList();

    private static string? UniqueId(XElement r, string type) =>
        r.Elements("uniqueid")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("type"), type, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static string? Tmdb(XElement r) => UniqueId(r, "tmdb") ?? Str(r, "tmdbid");

    private static string? Tvdb(XElement r) => UniqueId(r, "tvdb") ?? Str(r, "tvdbid");

    private static string? Imdb(XElement r)
    {
        var id = UniqueId(r, "imdb") ?? Str(r, "imdbid");
        if (id is not null)
        {
            return id;
        }
        // Legacy Kodi: <id> holds the IMDb id (tt…).
        var legacy = Str(r, "id");
        return legacy is not null && legacy.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? legacy : null;
    }

    private static int? YearOf(string? date) =>
        !string.IsNullOrEmpty(date) && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;

    private static DateOnly? DateOf(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static TvShowStatus? ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "continuing" or "returning series" or "in production" => TvShowStatus.Continuing,
        "ended" or "canceled" or "cancelled" => TvShowStatus.Ended,
        _ => null,
    };
}
