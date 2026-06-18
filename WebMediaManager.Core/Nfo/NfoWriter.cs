using System.Globalization;
using System.Xml.Linq;
using WebMediaManager.Core.Domain;

namespace WebMediaManager.Core.Nfo;

public interface INfoWriter
{
    XDocument BuildMovieNfo(Movie movie);

    XDocument BuildTvShowNfo(TvShow show);

    XDocument BuildEpisodeNfo(TvShow show, Episode episode);
}

/// <summary>
/// Builds Kodi-format NFO documents (read natively by Jellyfin and Emby). Provider IDs are dual-written —
/// modern <c>&lt;uniqueid&gt;</c> plus legacy <c>&lt;tmdbid&gt;/&lt;imdbid&gt;/&lt;tvdbid&gt;</c> elements —
/// since different player versions read different tags. Empty values are omitted. Pure: no IO.
/// </summary>
public sealed class NfoWriter : INfoWriter
{
    public XDocument BuildMovieNfo(Movie m)
    {
        var root = new XElement("movie");
        AddText(root, "title", m.Title);
        AddText(root, "originaltitle", m.OriginalTitle);
        AddText(root, "sorttitle", m.SortTitle);
        AddText(root, "year", m.Year?.ToString(CultureInfo.InvariantCulture));
        AddText(root, "plot", m.Plot);
        AddText(root, "tagline", m.Tagline);
        AddText(root, "runtime", m.Runtime?.ToString(CultureInfo.InvariantCulture));
        AddRating(root, m.Rating);
        AddText(root, "premiered", m.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddRepeated(root, "genre", m.Genres);
        AddRepeated(root, "studio", m.Studios);

        // Movies prefer TMDB as the default id.
        AddIds(root, defaultType: "tmdb", tmdb: m.TmdbId, imdb: m.ImdbId, tvdb: null);

        return Document(root);
    }

    public XDocument BuildTvShowNfo(TvShow s)
    {
        var root = new XElement("tvshow");
        AddText(root, "title", s.Title);
        AddText(root, "originaltitle", s.OriginalTitle);
        AddText(root, "year", s.Year?.ToString(CultureInfo.InvariantCulture));
        AddText(root, "plot", s.Plot);
        if (s.Status != TvShowStatus.Unknown)
        {
            AddText(root, "status", s.Status == TvShowStatus.Continuing ? "Continuing" : "Ended");
        }
        AddRating(root, s.Rating);
        AddRepeated(root, "genre", s.Genres);
        AddRepeated(root, "studio", s.Studios);

        // Shows prefer TVDB as the default id, falling back to TMDB.
        var defaultType = !string.IsNullOrEmpty(s.TvdbId) ? "tvdb" : "tmdb";
        AddIds(root, defaultType, tmdb: s.TmdbId, imdb: s.ImdbId, tvdb: s.TvdbId);

        return Document(root);
    }

    public XDocument BuildEpisodeNfo(TvShow show, Episode e)
    {
        var root = new XElement("episodedetails");
        AddText(root, "title", e.Title);
        AddText(root, "showtitle", show.Title);
        AddText(root, "season", e.SeasonNumber.ToString(CultureInfo.InvariantCulture));
        AddText(root, "episode", e.EpisodeNumber.ToString(CultureInfo.InvariantCulture));
        AddText(root, "plot", e.Plot);
        AddText(root, "aired", e.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddRating(root, e.Rating);

        var defaultType = !string.IsNullOrEmpty(e.TvdbId) ? "tvdb" : "tmdb";
        AddIds(root, defaultType, tmdb: e.TmdbId, imdb: null, tvdb: e.TvdbId);

        return Document(root);
    }

    private static XDocument Document(XElement root) =>
        new(new XDeclaration("1.0", "utf-8", "yes"), root);

    private static void AddText(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parent.Add(new XElement(name, value));
        }
    }

    private static void AddRepeated(XElement parent, string name, IEnumerable<string> values)
    {
        foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            parent.Add(new XElement(name, v));
        }
    }

    private static void AddRating(XElement parent, double? rating)
    {
        if (rating is { } r)
        {
            parent.Add(new XElement("rating", r.ToString("0.0", CultureInfo.InvariantCulture)));
        }
    }

    private static void AddIds(XElement parent, string defaultType, string? tmdb, string? imdb, string? tvdb)
    {
        // Modern <uniqueid> elements, with one marked default.
        AddUniqueId(parent, "tmdb", tmdb, defaultType);
        AddUniqueId(parent, "imdb", imdb, defaultType);
        AddUniqueId(parent, "tvdb", tvdb, defaultType);

        // Legacy elements for older scrapers.
        AddText(parent, "tmdbid", tmdb);
        AddText(parent, "imdbid", imdb);
        AddText(parent, "tvdbid", tvdb);
        AddText(parent, "id", imdb); // Kodi legacy: <id> is the IMDb id
    }

    private static void AddUniqueId(XElement parent, string type, string? value, string defaultType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var el = new XElement("uniqueid", new XAttribute("type", type), value);
        if (type == defaultType)
        {
            el.SetAttributeValue("default", "true");
        }
        parent.Add(el);
    }
}
