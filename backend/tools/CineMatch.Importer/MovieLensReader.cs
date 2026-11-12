using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CineMatch.Core.Models;

namespace CineMatch.Importer;

/// <summary>Reads both MovieLens file formats: CSV with a header row, and "::"-separated .dat files.</summary>
public partial class MovieLensReader(Dataset dataset, string dataDirectory)
{
    private const int MaxTagsPerMovie = 20;

    [GeneratedRegex(@"^(.*)\s\((\d{4})(?:\D\d{0,4})?\)$")]
    private static partial Regex TitleWithYear();

    [GeneratedRegex(@"^(.*), (The|A|An)$")]
    private static partial Regex TrailingArticle();

    /// <summary>All movies, with tags and TMDB ids attached where the dataset has them.</summary>
    public Dictionary<int, Movie> ReadMovies()
    {
        var movies = new Dictionary<int, Movie>();
        foreach (var row in ReadRows("movies"))
        {
            var (title, year) = SplitTitle(row[1]);
            var id = int.Parse(row[0], CultureInfo.InvariantCulture);
            movies[id] = new Movie
            {
                Id = id,
                Title = title,
                Year = year,
                Genres = row[2].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(g => !g.StartsWith('('))   // "(no genres listed)"
                    .ToList(),
            };
        }

        if (dataset.HasTags) AttachTags(movies);
        if (dataset.HasLinks) AttachTmdbIds(movies);
        foreach (var movie in movies.Values) movie.Description = DescribeFromMetadata(movie);
        return movies;
    }

    /// <summary>Streams the ratings without loading the file into memory.</summary>
    public IEnumerable<Rating> ReadRatings()
    {
        var separator = dataset.Csv ? "," : "::";
        var lines = File.ReadLines(dataset.File(dataDirectory, "ratings"), dataset.Encoding);
        foreach (var line in dataset.Csv ? lines.Skip(1) : lines)
        {
            if (line.Length > 0) yield return ParseRating(line, separator);
        }
    }

    // Rating lines hold only numbers, so a hand-written split is much faster than a CSV library.
    private static Rating ParseRating(ReadOnlySpan<char> line, ReadOnlySpan<char> separator)
    {
        var userId = int.Parse(NextField(ref line, separator), CultureInfo.InvariantCulture);
        var movieId = int.Parse(NextField(ref line, separator), CultureInfo.InvariantCulture);
        var value = double.Parse(NextField(ref line, separator), CultureInfo.InvariantCulture);
        var seconds = long.Parse(NextField(ref line, separator), CultureInfo.InvariantCulture);
        return new Rating
        {
            UserId = userId,
            MovieId = movieId,
            Value = value,
            Ts = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime,
        };
    }

    private static ReadOnlySpan<char> NextField(ref ReadOnlySpan<char> line, ReadOnlySpan<char> separator)
    {
        var end = line.IndexOf(separator);
        if (end < 0)
        {
            var last = line;
            line = [];
            return last;
        }
        var field = line[..end];
        line = line[(end + separator.Length)..];
        return field;
    }

    /// <summary>"Matrix, The (1999)" becomes ("The Matrix", 1999).</summary>
    public static (string Title, int? Year) SplitTitle(string raw)
    {
        var title = raw.Trim();
        int? year = null;

        var withYear = TitleWithYear().Match(title);
        if (withYear.Success)
        {
            title = withYear.Groups[1].Value.Trim();
            year = int.Parse(withYear.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var article = TrailingArticle().Match(title);
        if (article.Success) title = $"{article.Groups[2].Value} {article.Groups[1].Value}";
        return (title, year);
    }

    private void AttachTags(Dictionary<int, Movie> movies)
    {
        var counts = new Dictionary<int, Dictionary<string, int>>();
        foreach (var row in ReadRows("tags"))
        {
            var movieId = int.Parse(row[1], CultureInfo.InvariantCulture);
            var tag = row[2].Trim().ToLowerInvariant();
            if (tag.Length is 0 or > 40) continue;

            if (!counts.TryGetValue(movieId, out var movieTags)) counts[movieId] = movieTags = [];
            movieTags[tag] = movieTags.GetValueOrDefault(tag) + 1;
        }

        foreach (var (movieId, movieTags) in counts)
        {
            if (!movies.TryGetValue(movieId, out var movie)) continue;
            movie.Tags = movieTags
                .OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal)
                .Take(MaxTagsPerMovie)
                .Select(t => t.Key)
                .ToList();
        }
    }

    private void AttachTmdbIds(Dictionary<int, Movie> movies)
    {
        foreach (var row in ReadRows("links"))
        {
            if (row.Length > 2
                && int.TryParse(row[2], CultureInfo.InvariantCulture, out var tmdbId)
                && movies.TryGetValue(int.Parse(row[0], CultureInfo.InvariantCulture), out var movie))
            {
                movie.TmdbId = tmdbId;
            }
        }
    }

    /// <summary>A stand-in description for movies that have none from TMDB.</summary>
    private static string DescribeFromMetadata(Movie movie)
    {
        var kind = movie.Genres.Count > 0 ? string.Join(", ", movie.Genres) + " film" : "Film";
        var text = movie.Year is { } year ? $"{kind} from {year}." : $"{kind}.";
        if (movie.Tags.Count > 0) text += $" Viewers tagged it: {string.Join(", ", movie.Tags.Take(8))}.";
        return text;
    }

    private IEnumerable<string[]> ReadRows(string name)
    {
        var path = dataset.File(dataDirectory, name);
        if (!dataset.Csv)
        {
            foreach (var line in File.ReadLines(path, dataset.Encoding))
            {
                if (line.Length > 0) yield return line.Split("::");
            }
            yield break;
        }

        using var reader = new StreamReader(path, dataset.Encoding);
        foreach (var row in ReadCsv(reader).Skip(1))   // skip the header row
        {
            yield return row;
        }
    }

    /// <summary>A small CSV parser: quoted fields, doubled quotes, and line breaks inside quotes.</summary>
    public static IEnumerable<string[]> ReadCsv(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowStarted = false;

        int next;
        while ((next = reader.Read()) >= 0)
        {
            var ch = (char)next;
            if (inQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');   // "" inside quotes is one literal quote
                }
                else
                {
                    inQuotes = false;
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    rowStarted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    rowStarted = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (rowStarted || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        yield return fields.ToArray();
                    }
                    fields.Clear();
                    field.Clear();
                    rowStarted = false;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (rowStarted || field.Length > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}
