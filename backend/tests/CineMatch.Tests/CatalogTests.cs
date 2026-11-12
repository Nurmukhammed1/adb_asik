using System.Net;
using System.Net.Http.Json;
using CineMatch.Api.Contracts;
using CineMatch.Core.Models;
using MongoDB.Driver;

namespace CineMatch.Tests;

[Collection(ApiCollection.Name)]
public class CatalogTests(ApiFixture api)
{
    private static readonly MovieRequest ValidMovie = new()
    {
        Title = "  A New Movie  ",
        Year = 2026,
        Genres = ["Drama", "drama", "Sci-Fi"],
        Description = "Created by a test.",
        Tags = ["test"],
    };

    // ---------- Search ----------

    [Fact]
    public async Task Text_search_finds_title_and_tag_matches_and_ranks_the_title_match_first()
    {
        var word = ApiFixture.Unique("word");
        var inTags = await api.NewMovieAsync(tags: [word]);
        var inTitle = await api.NewMovieAsync(title: $"The {word} Returns");
        await api.NewMovieAsync(); // unrelated

        var result = await api.Anonymous().GetAsync<PagedResult<MovieSummary>>($"/api/movies?q={word}");

        Assert.Equal(2, result.Total);
        Assert.Equal([inTitle.Id, inTags.Id], result.Items.Select(m => m.Id));
    }

    [Fact]
    public async Task Genre_and_year_filters_combine()
    {
        var genre = ApiFixture.Unique("genre");
        await api.NewMovieAsync(genres: [genre], year: 1990);
        var match = await api.NewMovieAsync(genres: [genre, "Drama"], year: 2000);
        await api.NewMovieAsync(genres: [genre], year: 2010);
        await api.NewMovieAsync(genres: ["Drama"], year: 2000);

        var result = await api.Anonymous().GetAsync<PagedResult<MovieSummary>>(
            $"/api/movies?genre={genre}&yearFrom=1995&yearTo=2005");

        Assert.Equal([match.Id], result.Items.Select(m => m.Id));
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task Sorting_by_rating_and_the_minimum_ratings_filter_use_the_stored_statistics()
    {
        var genre = ApiFixture.Unique("genre");
        var unrated = await api.NewMovieAsync(genres: [genre]);
        var good = await api.NewMovieAsync(genres: [genre]);
        var best = await api.NewMovieAsync(genres: [genre]);
        var (first, _) = await api.NewUserAsync();
        var (second, _) = await api.NewUserAsync();
        await first.RateAsync(best.Id, 5);
        await second.RateAsync(best.Id, 4.5);
        await first.RateAsync(good.Id, 4);

        var client = api.Anonymous();
        var byRating = await client.GetAsync<PagedResult<MovieSummary>>($"/api/movies?genre={genre}&sort=rating");
        var byPopularity = await client.GetAsync<PagedResult<MovieSummary>>($"/api/movies?genre={genre}");
        var wellRated = await client.GetAsync<PagedResult<MovieSummary>>($"/api/movies?genre={genre}&minRatings=2");

        Assert.Equal([best.Id, good.Id, unrated.Id], byRating.Items.Select(m => m.Id));
        Assert.Equal([best.Id, good.Id, unrated.Id], byPopularity.Items.Select(m => m.Id));
        Assert.Equal([best.Id], wellRated.Items.Select(m => m.Id));
        Assert.Equal(4.75, byRating.Items[0].RatingStats.Avg);
    }

    [Fact]
    public async Task Pages_do_not_overlap_and_together_cover_every_result()
    {
        var genre = ApiFixture.Unique("genre");
        for (var i = 0; i < 5; i++) await api.NewMovieAsync(genres: [genre]);
        var client = api.Anonymous();

        var pages = new List<PagedResult<MovieSummary>>();
        for (var page = 1; page <= 3; page++)
            pages.Add(await client.GetAsync<PagedResult<MovieSummary>>($"/api/movies?genre={genre}&sort=title&page={page}&pageSize=2"));

        Assert.All(pages, p => Assert.Equal(5, p.Total));
        Assert.Equal([2, 2, 1], pages.Select(p => p.Items.Count));
        Assert.Equal(5, pages.SelectMany(p => p.Items).Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public async Task Page_size_is_capped_and_bad_paging_values_are_corrected()
    {
        var result = await api.Anonymous().GetAsync<PagedResult<MovieSummary>>("/api/movies?page=0&pageSize=5000");

        Assert.Equal(1, result.Page);
        Assert.Equal(MovieQuery.MaxPageSize, result.PageSize);
    }

    [Fact]
    public async Task An_unknown_sort_value_is_a_bad_request()
    {
        var response = await api.Anonymous().GetAsync("/api/movies?sort=banana");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Genres_lists_each_genre_once_even_when_some_movies_have_none()
    {
        var genre = ApiFixture.Unique("genre");
        await api.NewMovieAsync(genres: [genre]);
        await api.NewMovieAsync(genres: [genre]);
        await api.NewMovieAsync(genres: []); // made the distinct query fail on MongoDB 8 before the fix

        var genres = await api.Anonymous().GetAsync<List<string>>("/api/genres");

        Assert.Single(genres, g => g == genre);
        Assert.DoesNotContain(null, genres);
    }

    // ---------- Details ----------

    [Fact]
    public async Task Movie_details_include_the_callers_rating_and_watchlist_state_only_when_logged_in()
    {
        var movie = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();
        await client.RateAsync(movie.Id, 3.5);
        await client.PostAsync($"/api/users/me/watchlist/{movie.Id}", null);

        var mine = await client.GetAsync<MovieDetailsResponse>($"/api/movies/{movie.Id}");
        var anonymous = await api.Anonymous().GetAsync<MovieDetailsResponse>($"/api/movies/{movie.Id}");

        Assert.Equal(3.5, mine.MyRating);
        Assert.True(mine.InWatchlist);
        Assert.Null(anonymous.MyRating);
        Assert.False(anonymous.InWatchlist);
        Assert.Equal("A test movie.", anonymous.Movie.Description);
    }

    [Fact]
    public async Task An_unknown_movie_is_not_found()
    {
        var response = await api.Anonymous().GetAsync("/api/movies/2000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Catalog management ----------

    [Fact]
    public async Task Only_admins_can_change_the_catalog()
    {
        var movie = await api.NewMovieAsync();
        var (user, _) = await api.NewUserAsync();
        var anonymous = api.Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/movies", ValidMovie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/movies", ValidMovie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/movies/{movie.Id}", ValidMovie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/movies/{movie.Id}")).StatusCode);

        // Nothing changed.
        var details = await anonymous.GetAsync<MovieDetailsResponse>($"/api/movies/{movie.Id}");
        Assert.Equal(movie.Title, details.Movie.Title);
    }

    [Fact]
    public async Task Admin_creates_a_movie_with_cleaned_input_and_empty_statistics()
    {
        var admin = await api.NewAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/movies", ValidMovie);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<Movie>();
        Assert.Equal("A New Movie", created.Title);
        Assert.Equal(["Drama", "Sci-Fi"], created.Genres);
        Assert.Equal(0, created.RatingStats.Count);
        Assert.EndsWith($"/api/movies/{created.Id}", response.Headers.Location!.ToString());

        var stored = await api.Anonymous().GetAsync<MovieDetailsResponse>($"/api/movies/{created.Id}");
        Assert.Equal("Created by a test.", stored.Movie.Description);
    }

    [Fact]
    public async Task Invalid_movie_input_lists_every_bad_field()
    {
        var admin = await api.NewAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/movies",
            new MovieRequest { Title = "", Genres = [], Description = "", Year = 1500 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadErrorFieldsAsync();
        Assert.Contains("Title", fields);
        Assert.Contains("Genres", fields);
        Assert.Contains("Description", fields);
        Assert.Contains("Year", fields);
    }

    [Fact]
    public async Task Updating_a_movie_keeps_its_rating_statistics()
    {
        var movie = await api.NewMovieAsync();
        var (user, _) = await api.NewUserAsync();
        await user.RateAsync(movie.Id, 4);
        var admin = await api.NewAdminAsync();

        var response = await admin.PutAsJsonAsync($"/api/movies/{movie.Id}",
            new MovieRequest { Title = "Renamed", Year = null, Genres = ["Comedy"], Description = "New text." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<Movie>();
        Assert.Equal("Renamed", updated.Title);
        Assert.Null(updated.Year);
        Assert.Equal(["Comedy"], updated.Genres);
        Assert.Equal(1, updated.RatingStats.Count);
        Assert.Equal(4, updated.RatingStats.Avg);

        var missing = await admin.PutAsJsonAsync("/api/movies/2000000000", ValidMovie);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_movie_also_deletes_its_ratings_and_watchlist_entries()
    {
        var movie = await api.NewMovieAsync();
        var kept = await api.NewMovieAsync();
        var (user, _) = await api.NewUserAsync();
        foreach (var id in new[] { movie.Id, kept.Id })
        {
            await user.RateAsync(id, 4);
            await user.PostAsync($"/api/users/me/watchlist/{id}", null);
        }
        var admin = await api.NewAdminAsync();

        var response = await admin.DeleteAsync($"/api/movies/{movie.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/movies/{movie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/movies/{movie.Id}")).StatusCode);
        Assert.Equal(0, await api.Mongo.Ratings.CountDocumentsAsync(r => r.MovieId == movie.Id));
        Assert.Equal(0, await api.Mongo.Watchlist.CountDocumentsAsync(w => w.MovieId == movie.Id));

        var history = await user.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history");
        var watchlist = await user.GetAsync<PagedResult<WatchlistEntry>>("/api/users/me/watchlist");
        Assert.Equal([kept.Id], history.Items.Select(i => i.Movie.Id));
        Assert.Equal([kept.Id], watchlist.Items.Select(i => i.Movie.Id));
    }
}
