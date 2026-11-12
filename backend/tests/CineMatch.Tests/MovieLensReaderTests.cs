using System.Text;
using CineMatch.Importer;

namespace CineMatch.Tests;

/// <summary>The importer's file parsing. These tests need no database.</summary>
public class MovieLensReaderTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "cinematch-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    [Theory]
    [InlineData("Toy Story (1995)", "Toy Story", 1995)]
    [InlineData("Matrix, The (1999)", "The Matrix", 1999)]
    [InlineData("Beautiful Mind, A (2001)", "A Beautiful Mind", 2001)]
    [InlineData("Seven (a.k.a. Se7en) (1995)", "Seven (a.k.a. Se7en)", 1995)]
    [InlineData("Babylon 5", "Babylon 5", null)]
    [InlineData("  Heat (1995)  ", "Heat", 1995)]
    [InlineData("Doctor Who (2005–2013)", "Doctor Who", 2005)]
    public void Title_and_year_are_split_and_a_trailing_article_moves_to_the_front(string raw, string title, int? year)
    {
        Assert.Equal((title, year), MovieLensReader.SplitTitle(raw));
    }

    [Fact]
    public void Csv_parser_handles_quotes_commas_and_line_breaks_inside_fields()
    {
        const string csv = "id,title,genres\r\n" +
                           "1,\"American President, The (1995)\",Comedy|Drama\r\n" +
                           "2,\"He said \"\"hi\"\"\",\n" +
                           "3,\"two\nlines\",Drama\n" +
                           "\n" +
                           "4,last,";

        var rows = MovieLensReader.ReadCsv(new StringReader(csv)).ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal(["id", "title", "genres"], rows[0]);
        Assert.Equal(["1", "American President, The (1995)", "Comedy|Drama"], rows[1]);
        Assert.Equal(["2", "He said \"hi\"", ""], rows[2]);
        Assert.Equal(["3", "two\nlines", "Drama"], rows[3]);
        Assert.Equal(["4", "last", ""], rows[4]);
    }

    [Fact]
    public void Csv_dataset_is_read_with_tags_links_and_a_stand_in_description()
    {
        var dataset = Dataset.All["small"];
        Write(dataset, "movies", "movieId,title,genres\n1,\"Matrix, The (1999)\",Action|Sci-Fi\n2,Untitled,(no genres listed)\n");
        Write(dataset, "tags", "userId,movieId,tag,timestamp\n1,1,Cyberpunk,1\n2,1,cyberpunk,2\n3,1,keanu,3\n4,99,orphan,4\n");
        Write(dataset, "links", "movieId,imdbId,tmdbId\n1,0133093,603\n2,0000000,\n");
        Write(dataset, "ratings", "userId,movieId,rating,timestamp\n7,1,4.5,946684800\n8,2,3.0,946684801\n");
        var reader = new MovieLensReader(dataset, _dataDirectory);

        var movies = reader.ReadMovies();
        var ratings = reader.ReadRatings().ToList();

        var matrix = movies[1];
        Assert.Equal("The Matrix", matrix.Title);
        Assert.Equal(1999, matrix.Year);
        Assert.Equal(["Action", "Sci-Fi"], matrix.Genres);
        Assert.Equal(["cyberpunk", "keanu"], matrix.Tags); // lower-cased, most used first
        Assert.Equal(603, matrix.TmdbId);
        Assert.Equal("Action, Sci-Fi film from 1999. Viewers tagged it: cyberpunk, keanu.", matrix.Description);

        var untitled = movies[2];
        Assert.Empty(untitled.Genres);
        Assert.Null(untitled.Year);
        Assert.Null(untitled.TmdbId);
        Assert.Equal("Film.", untitled.Description);

        Assert.Equal(2, ratings.Count);
        Assert.Equal((7, 1, 4.5), (ratings[0].UserId, ratings[0].MovieId, ratings[0].Value));
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), ratings[0].Ts);
    }

    [Fact]
    public void Dat_dataset_with_double_colon_separators_is_read()
    {
        var dataset = Dataset.All["10m"];
        Write(dataset, "movies", "1::Toy Story (1995)::Adventure|Animation\n");
        Write(dataset, "tags", "15::1::Pixar::1215184630\n");
        Write(dataset, "ratings", "1::1::5::838985046\n2::1::3.5::838985047\n");
        var reader = new MovieLensReader(dataset, _dataDirectory);

        var movies = reader.ReadMovies();
        var ratings = reader.ReadRatings().ToList();

        Assert.Equal("Toy Story", movies[1].Title);
        Assert.Equal(["Adventure", "Animation"], movies[1].Genres);
        Assert.Equal(["pixar"], movies[1].Tags);
        Assert.Equal([5.0, 3.5], ratings.Select(r => r.Value));
        Assert.Equal([1, 2], ratings.Select(r => r.UserId));
    }

    private void Write(Dataset dataset, string name, string content)
    {
        var path = dataset.File(_dataDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
