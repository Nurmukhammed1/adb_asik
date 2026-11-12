using System.Net;
using System.Net.Http.Json;
using CineMatch.Api.Contracts;
using CineMatch.Api.Controllers;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Recommender;
using MongoDB.Driver;

namespace CineMatch.Tests;

/// <summary>The recommendation endpoints, from stored ratings through the rebuild to the returned lists.</summary>
[Collection(ApiCollection.Name)]
public class RecommendationApiTests(ApiFixture api)
{
    /// <summary>
    /// A crowd of six users who all gave 5 stars to the "liked" movies and 1 star to the "disliked" ones.
    /// That makes the liked movies neighbors of each other, and likewise the disliked ones.
    /// </summary>
    private sealed record TasteGroup(Movie[] Liked, Movie[] Disliked);

    private async Task<TasteGroup> SeedTasteGroupAsync()
    {
        var liked = new[] { await api.NewMovieAsync(), await api.NewMovieAsync(), await api.NewMovieAsync() };
        var disliked = new[] { await api.NewMovieAsync(), await api.NewMovieAsync() };

        // The crowd needs no accounts, only ratings. Their ids come from the real sequence, so they never
        // collide with registered users.
        var ratings = new List<Rating>();
        for (var n = 0; n < 6; n++)
        {
            var fan = await api.Mongo.NextIdAsync(MongoContext.UsersName);
            ratings.AddRange(liked.Select(m => new Rating { UserId = fan, MovieId = m.Id, Value = 5, Ts = DateTime.UtcNow }));
            ratings.AddRange(disliked.Select(m => new Rating { UserId = fan, MovieId = m.Id, Value = 1, Ts = DateTime.UtcNow }));
        }
        await api.Mongo.Ratings.InsertManyAsync(ratings);
        return new TasteGroup(liked, disliked);
    }

    /// <summary>A user who agrees with the crowd on two liked and one disliked movie (five ratings in total).</summary>
    private async Task<HttpClient> NewUserWhoSharesTheTasteAsync(TasteGroup group)
    {
        var (client, _) = await api.NewUserAsync();
        await client.RateAsync(group.Liked[0].Id, 5);
        await client.RateAsync(group.Liked[1].Id, 5);
        await client.RateAsync(group.Disliked[0].Id, 1);
        await client.RateAsync((await api.NewMovieAsync()).Id, 3);
        await client.RateAsync((await api.NewMovieAsync()).Id, 3);
        return client;
    }

    private static async Task<RebuildResult> RebuildAsync(HttpClient admin)
    {
        var response = await admin.PostAsync("/api/admin/recommender/rebuild?wait=true", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<RebuildResult>();
    }

    // ---------- Personal recommendations ----------

    [Fact]
    public async Task A_user_gets_the_movie_that_people_with_the_same_taste_liked()
    {
        var group = await SeedTasteGroupAsync();
        var client = await NewUserWhoSharesTheTasteAsync(group);
        await RebuildAsync(await api.NewAdminAsync());

        var list = await client.GetAsync<RecommendationList>("/api/recommendations/me?limit=5");

        Assert.Equal(RecommendationSources.Collaborative, list.Strategy);
        var first = list.Items[0];
        Assert.Equal(group.Liked[2].Id, first.Movie.Id);   // the one liked movie the user has not seen
        Assert.Equal(RecommendationSources.Collaborative, first.Source);
        Assert.Contains(first.BecauseOf!.Id, new[] { group.Liked[0].Id, group.Liked[1].Id });
        Assert.InRange(first.Score, 3.4, 5);               // predicted above the user's 3.4 average

        // The unseen disliked movie is a neighbor of one the user rated 1 star: it must not be recommended.
        Assert.DoesNotContain(list.Items, i => i.Movie.Id == group.Disliked[1].Id);
    }

    [Fact]
    public async Task Recommendations_never_contain_a_movie_the_user_already_rated()
    {
        var group = await SeedTasteGroupAsync();
        var client = await NewUserWhoSharesTheTasteAsync(group);
        await RebuildAsync(await api.NewAdminAsync());

        var list = await client.GetAsync<RecommendationList>("/api/recommendations/me?limit=100");
        var history = await client.GetAsync<PagedResult<HistoryItem>>("/api/users/me/history");

        var rated = history.Items.Select(i => i.Movie.Id).ToHashSet();
        Assert.Equal(5, rated.Count);
        Assert.DoesNotContain(list.Items, i => rated.Contains(i.Movie.Id));
        Assert.Equal(list.Items.Count, list.Items.Select(i => i.Movie.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_new_rating_changes_the_recommendations_without_a_rebuild()
    {
        var group = await SeedTasteGroupAsync();
        var client = await NewUserWhoSharesTheTasteAsync(group);
        await RebuildAsync(await api.NewAdminAsync());

        await client.RateAsync(group.Liked[2].Id, 5);   // the user watches the recommended movie
        var list = await client.GetAsync<RecommendationList>("/api/recommendations/me?limit=5");

        Assert.DoesNotContain(list.Items, i => i.Movie.Id == group.Liked[2].Id);
    }

    [Fact]
    public async Task A_new_user_gets_popular_movies_from_their_favorite_genres_first()
    {
        var genre = ApiFixture.Unique("genre");
        var good = await api.NewMovieAsync(genres: [genre]);
        var weak = await api.NewMovieAsync(genres: [genre]);
        var outsideGenre = await api.NewMovieAsync();
        var (rater, _) = await api.NewUserAsync();
        await rater.RateAsync(good.Id, 5);
        await rater.RateAsync(weak.Id, 2);
        await rater.RateAsync(outsideGenre.Id, 5);   // so the list can be filled even if this test runs first

        var (newcomer, _) = await api.NewUserAsync();
        await newcomer.PutAsJsonAsync("/api/users/me", new UpdateProfileRequest { DisplayName = "New", FavoriteGenres = [genre] });

        var list = await newcomer.GetAsync<RecommendationList>("/api/recommendations/me?limit=3");

        Assert.Equal(RecommendationSources.Popular, list.Strategy);
        Assert.Equal([good.Id, weak.Id], list.Items.Take(2).Select(i => i.Movie.Id));
        Assert.All(list.Items, i =>
        {
            Assert.Equal(RecommendationSources.Popular, i.Source);
            Assert.Null(i.BecauseOf);
        });
        Assert.Equal(3, list.Items.Count);   // filled up from outside the favorite genre
    }

    [Fact]
    public async Task Recommendations_need_a_login_and_cap_the_limit()
    {
        var (client, _) = await api.NewUserAsync();

        var anonymous = await api.Anonymous().GetAsync("/api/recommendations/me");
        var capped = await client.GetAsync<RecommendationList>("/api/recommendations/me?limit=100000");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(capped.Items.Count <= RecommendationService.MaxLimit);
    }

    // ---------- Similar and popular ----------

    [Fact]
    public async Task Similar_movies_are_the_ones_the_same_people_rated_the_same_way()
    {
        var group = await SeedTasteGroupAsync();
        await RebuildAsync(await api.NewAdminAsync());
        var client = api.Anonymous();

        var similar = await client.GetAsync<List<Recommendation>>($"/api/movies/{group.Liked[0].Id}/similar");

        Assert.Equal(
            new[] { group.Liked[1].Id, group.Liked[2].Id }.Order(),
            similar.Select(i => i.Movie.Id).Order());
        Assert.All(similar, i =>
        {
            Assert.Equal(RecommendationSources.Similar, i.Source);
            Assert.InRange(i.Score, 0.01, 1);
        });

        var limited = await client.GetAsync<List<Recommendation>>($"/api/movies/{group.Liked[0].Id}/similar?limit=1");
        Assert.Single(limited);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/movies/2000000000/similar")).StatusCode);
    }

    [Fact]
    public async Task A_movie_without_neighbors_falls_back_to_popular_movies_of_its_genre()
    {
        var genre = ApiFixture.Unique("genre");
        var lonely = await api.NewMovieAsync(genres: [genre]);
        var sameGenre = await api.NewMovieAsync(genres: [genre]);
        var (rater, _) = await api.NewUserAsync();
        await rater.RateAsync(sameGenre.Id, 4);

        var similar = await api.Anonymous().GetAsync<List<Recommendation>>($"/api/movies/{lonely.Id}/similar");

        var only = Assert.Single(similar);
        Assert.Equal(sameGenre.Id, only.Movie.Id);
        Assert.Equal(RecommendationSources.Popular, only.Source);
    }

    [Fact]
    public async Task A_deleted_movie_disappears_from_similar_lists_and_recommendations_at_once()
    {
        var group = await SeedTasteGroupAsync();
        var client = await NewUserWhoSharesTheTasteAsync(group);
        var admin = await api.NewAdminAsync();
        await RebuildAsync(admin);

        // The other movies' stored neighbor lists still name it; they are only refreshed by the next rebuild.
        await admin.DeleteAsync($"/api/movies/{group.Liked[2].Id}");

        var similar = await client.GetAsync<List<Recommendation>>($"/api/movies/{group.Liked[0].Id}/similar");
        var list = await client.GetAsync<RecommendationList>("/api/recommendations/me?limit=5");

        Assert.Equal([group.Liked[1].Id], similar.Select(i => i.Movie.Id));
        Assert.DoesNotContain(list.Items, i => i.Movie.Id == group.Liked[2].Id);
    }

    [Fact]
    public async Task Popular_ranks_a_well_rated_movie_with_many_ratings_above_a_perfect_one_with_few()
    {
        var genre = ApiFixture.Unique("genre");
        var fewPerfect = await api.NewMovieAsync(genres: [genre]);
        var manyGreat = await api.NewMovieAsync(genres: [genre]);
        await api.NewMovieAsync(genres: [genre]);   // unrated: must not appear
        await SetStatsAsync(fewPerfect.Id, count: 2, sum: 10);       // average 5.0
        await SetStatsAsync(manyGreat.Id, count: 400, sum: 1800);    // average 4.5

        var popular = await api.Anonymous().GetAsync<List<Recommendation>>($"/api/movies/popular?genre={genre}");

        Assert.Equal([manyGreat.Id, fewPerfect.Id], popular.Select(i => i.Movie.Id));
        Assert.True(popular[0].Score > popular[1].Score);
        Assert.InRange(popular[0].Score, 4.3, 4.5);   // close to its real average
        Assert.All(popular, i => Assert.Equal(RecommendationSources.Popular, i.Source));
    }

    // ---------- Admin ----------

    [Fact]
    public async Task Rebuild_reports_what_it_processed_and_status_shows_it()
    {
        var group = await SeedTasteGroupAsync();
        var admin = await api.NewAdminAsync();

        var result = await RebuildAsync(admin);
        var status = await admin.GetAsync<RecommenderStatus>("/api/admin/recommender/status");

        Assert.True(result.Ratings >= 30);
        Assert.True(result.MoviesWithNeighbors >= group.Liked.Length + group.Disliked.Length);
        Assert.True(result.StoredPairs >= result.MoviesWithNeighbors);
        Assert.False(status.Running);
        Assert.Equal(result.MoviesWithNeighbors, status.MoviesWithNeighbors);
        Assert.Equal(result.StartedAt, status.LastRebuild!.StartedAt);
        Assert.Equal(RankBy.Evidence, status.Settings.RankBy);
        Assert.Equal(result.MoviesWithNeighbors, await api.Mongo.ItemSimilarities.CountDocumentsAsync(_ => true));
    }

    [Fact]
    public async Task Stored_neighbor_lists_are_sorted_and_within_the_configured_size()
    {
        await SeedTasteGroupAsync();
        await RebuildAsync(await api.NewAdminAsync());

        var stored = await api.Mongo.ItemSimilarities.Find(_ => true).ToListAsync();

        Assert.NotEmpty(stored);
        Assert.All(stored, entry =>
        {
            Assert.InRange(entry.Neighbors.Length, 1, 50);
            Assert.Equal(entry.Neighbors.OrderByDescending(n => n.Score).Select(n => n.Score), entry.Neighbors.Select(n => n.Score));
            Assert.DoesNotContain(entry.Neighbors, n => n.MovieId == entry.Id);
        });
    }

    [Fact]
    public async Task A_background_rebuild_is_accepted_and_finishes()
    {
        await SeedTasteGroupAsync();
        var admin = await api.NewAdminAsync();

        var response = await admin.PostAsync("/api/admin/recommender/rebuild", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var status = await admin.GetAsync<RecommenderStatus>("/api/admin/recommender/status");
        for (var attempt = 0; attempt < 100 && (status.Running || status.LastRebuild is null); attempt++)
        {
            await Task.Delay(100);
            status = await admin.GetAsync<RecommenderStatus>("/api/admin/recommender/status");
        }
        Assert.False(status.Running);
        Assert.NotNull(status.LastRebuild);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task The_accuracy_check_returns_a_report_and_rejects_bad_settings()
    {
        await SeedTasteGroupAsync();
        var admin = await api.NewAdminAsync();

        var response = await admin.PostAsync("/api/admin/recommender/evaluate?listSize=5", null);
        var invalid = await admin.PostAsync("/api/admin/recommender/evaluate?testFraction=1.5", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.ReadAsync<EvaluationReport>();
        Assert.True(report.Users > 0);
        Assert.True(report.TrainRatings > 0);
        Assert.Equal(5, report.ListSize);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/admin/recommender/status")]
    [InlineData("POST", "/api/admin/recommender/rebuild")]
    [InlineData("POST", "/api/admin/recommender/evaluate")]
    public async Task Recommender_administration_is_admin_only(string method, string path)
    {
        var (user, _) = await api.NewUserAsync();

        var asUser = await user.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        var anonymous = await api.Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private Task SetStatsAsync(int movieId, int count, double sum) =>
        api.Mongo.Movies.UpdateOneAsync(
            m => m.Id == movieId,
            Builders<Movie>.Update.Set(m => m.RatingStats, new RatingStats { Count = count, Sum = sum, Avg = sum / count }));
}
