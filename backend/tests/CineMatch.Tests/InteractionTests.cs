using System.Net;
using CineMatch.Api.Contracts;
using CineMatch.Core.Models;
using MongoDB.Driver;

namespace CineMatch.Tests;

[Collection(ApiCollection.Name)]
public class InteractionTests(ApiFixture api)
{
    // ---------- Ratings ----------

    [Fact]
    public async Task Rating_changing_and_removing_keep_the_movie_statistics_exact()
    {
        var movie = await api.NewMovieAsync();
        var (first, _) = await api.NewUserAsync();
        var (second, _) = await api.NewUserAsync();

        var created = await (await first.RateAsync(movie.Id, 4.5)).ReadAsync<RatingResponse>();
        Assert.Equal((1, 4.5, 4.5), Stats(created));

        var another = await (await second.RateAsync(movie.Id, 2)).ReadAsync<RatingResponse>();
        Assert.Equal((2, 6.5, 3.25), Stats(another));

        var changed = await (await first.RateAsync(movie.Id, 3)).ReadAsync<RatingResponse>();
        Assert.Equal((2, 5.0, 2.5), Stats(changed));

        var removed = await (await second.DeleteAsync($"/api/movies/{movie.Id}/rating")).ReadAsync<RatingResponse>();
        Assert.Equal((1, 3.0, 3.0), Stats(removed));
        Assert.Null(removed.Rating);

        var last = await (await first.DeleteAsync($"/api/movies/{movie.Id}/rating")).ReadAsync<RatingResponse>();
        Assert.Equal((0, 0.0, 0.0), Stats(last));

        // The response repeats what is stored.
        var stored = await first.GetAsync<MovieDetailsResponse>($"/api/movies/{movie.Id}");
        Assert.Equal(0, stored.Movie.RatingStats.Count);
        Assert.Null(stored.MyRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(4.3)]
    [InlineData(5.5)]
    [InlineData(-1)]
    public async Task Ratings_outside_the_half_star_scale_are_rejected(double rating)
    {
        var movie = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();

        var response = await client.RateAsync(movie.Id, rating);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await api.Mongo.Ratings.CountDocumentsAsync(r => r.MovieId == movie.Id));
    }

    [Fact]
    public async Task Rating_needs_a_login()
    {
        var movie = await api.NewMovieAsync();

        var response = await api.Anonymous().RateAsync(movie.Id, 4);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rating_an_unknown_movie_is_not_found_and_stores_nothing()
    {
        const int missingMovie = 2000000001;
        var (client, user) = await api.NewUserAsync();

        var response = await client.RateAsync(missingMovie, 4);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // The rating was written inside the transaction and must have been rolled back.
        Assert.Equal(0, await api.Mongo.Ratings.CountDocumentsAsync(r => r.UserId == user.Id));
    }

    [Fact]
    public async Task Removing_a_rating_that_does_not_exist_is_not_found()
    {
        var movie = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();

        var response = await client.DeleteAsync($"/api/movies/{movie.Id}/rating");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Many_users_rating_one_movie_at_the_same_time_lose_no_updates()
    {
        const int users = 12;
        var movie = await api.NewMovieAsync();
        var clients = new List<HttpClient>();
        for (var i = 0; i < users; i++) clients.Add((await api.NewUserAsync()).Client);

        // Round 1 creates the ratings, round 2 changes all of them; both hit the same movie document.
        foreach (var rating in new[] { 2.0, 4.5 })
        {
            var responses = await Task.WhenAll(clients.Select(c => c.RateAsync(movie.Id, rating)));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }

        var stored = await api.Anonymous().GetAsync<MovieDetailsResponse>($"/api/movies/{movie.Id}");
        Assert.Equal(users, stored.Movie.RatingStats.Count);
        Assert.Equal(users * 4.5, stored.Movie.RatingStats.Sum);
        Assert.Equal(4.5, stored.Movie.RatingStats.Avg);
        Assert.Equal(users, await api.Mongo.Ratings.CountDocumentsAsync(r => r.MovieId == movie.Id));
    }

    // ---------- History ----------

    [Fact]
    public async Task Rating_history_is_newest_first_with_movie_details()
    {
        var older = await api.NewMovieAsync();
        var newer = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();
        await client.RateAsync(older.Id, 3);
        await client.RateAsync(newer.Id, 5);

        var history = await client.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history");
        var firstPage = await client.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history?pageSize=1");

        Assert.Equal(2, history.Total);
        Assert.Equal([newer.Id, older.Id], history.Items.Select(i => i.Movie.Id));
        Assert.Equal([5.0, 3.0], history.Items.Select(i => i.Rating));
        Assert.All(history.Items, i => Assert.Equal("rating", i.Type));
        Assert.Equal(newer.Title, history.Items[0].Movie.Title);
        Assert.Equal(2, firstPage.Total);
        Assert.Single(firstPage.Items);
    }

    [Fact]
    public async Task Views_are_recorded_and_listed_newest_first()
    {
        var one = await api.NewMovieAsync();
        var two = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();

        foreach (var id in new[] { one.Id, two.Id, one.Id })
        {
            var response = await client.PostAsync($"/api/movies/{id}/views", null);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await Task.Delay(5); // distinct timestamps, so the order is defined
        }
        var unknown = await client.PostAsync("/api/movies/2000000000/views", null);

        var views = await client.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history?type=views");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(3, views.Total);
        Assert.Equal([one.Id, two.Id, one.Id], views.Items.Select(i => i.Movie.Id));
        Assert.All(views.Items, i => Assert.Equal("view", i.Type));
    }

    [Fact]
    public async Task One_users_history_never_shows_another_users_activity()
    {
        var movie = await api.NewMovieAsync();
        var (active, _) = await api.NewUserAsync();
        var (quiet, _) = await api.NewUserAsync();
        await active.RateAsync(movie.Id, 4);
        await active.PostAsync($"/api/movies/{movie.Id}/views", null);
        await active.PostAsync($"/api/users/me/watchlist/{movie.Id}", null);

        Assert.Equal(0, (await quiet.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history")).Total);
        Assert.Equal(0, (await quiet.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history?type=views")).Total);
        Assert.Equal(0, (await quiet.GetAsync<PagedResult<WatchlistEntry>>("/api/users/me/watchlist")).Total);
    }

    // ---------- Watchlist ----------

    [Fact]
    public async Task Watchlist_add_is_repeatable_and_remove_reports_missing_entries()
    {
        var first = await api.NewMovieAsync();
        var second = await api.NewMovieAsync();
        var (client, _) = await api.NewUserAsync();

        foreach (var id in new[] { first.Id, second.Id, first.Id })
        {
            var added = await client.PostAsync($"/api/users/me/watchlist/{id}", null);
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
            await Task.Delay(5);
        }
        var unknown = await client.PostAsync("/api/users/me/watchlist/2000000000", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var watchlist = await client.GetAsync<PagedResult<WatchlistEntry>>("/api/users/me/watchlist");
        Assert.Equal(2, watchlist.Total);
        Assert.Equal([second.Id, first.Id], watchlist.Items.Select(i => i.Movie.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/users/me/watchlist/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/users/me/watchlist/{first.Id}")).StatusCode);
        Assert.Equal(1, (await client.GetAsync<PagedResult<WatchlistEntry>>("/api/users/me/watchlist")).Total);
    }

    private static (int Count, double Sum, double Avg) Stats(RatingResponse response) =>
        (response.RatingStats.Count, response.RatingStats.Sum, response.RatingStats.Avg);
}
