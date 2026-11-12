using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Perf;

/// <summary>The fixed inputs of a run, chosen from the data so that every run on the same dataset uses the same ones.</summary>
public sealed record TestInputs(
    IReadOnlyList<int> MovieIds,
    IReadOnlyList<(int UserId, int Ratings)> TypicalUsers,
    IReadOnlyList<(int UserId, int Ratings)> HeavyUsers)
{
    public const int Movies = 200;
    public const int Typical = 20;
    public const int Heavy = 5;

    public static readonly string[] SearchTerms = ["star", "love", "night", "war", "king", "dark", "life", "world", "dead", "man"];
    public static readonly string[] Genres = ["Comedy", "Drama", "Action", "Thriller", "Sci-Fi"];

    public TestUsers Describe() => new(
        TypicalUsers.Count,
        TypicalUsers.Count > 0 ? TypicalUsers[TypicalUsers.Count / 2].Ratings : 0,
        HeavyUsers.Count,
        HeavyUsers.Count > 0 ? HeavyUsers[0].Ratings : 0);

    /// <summary>
    /// The most-rated movies (what people open most), the users around the median number of ratings
    /// ("typical"), and the users with the most ratings ("heavy", the worst case for recommendations).
    /// </summary>
    public static async Task<TestInputs> SelectAsync(MongoContext ctx)
    {
        var movieIds = await ctx.Movies.Find(Builders<Movie>.Filter.Empty)
            .SortByDescending(m => m.RatingStats.Count).ThenBy(m => m.Id)
            .Limit(Movies)
            .Project(m => m.Id)
            .ToListAsync();

        var counts = await ctx.Database.GetCollection<BsonDocument>(MongoContext.RatingsName)
            .Aggregate(new AggregateOptions { AllowDiskUse = true })
            .Group(new BsonDocument { { "_id", "$userId" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        var users = counts
            .Select(c => (UserId: c["_id"].ToInt32(), Ratings: c["n"].ToInt32()))
            .OrderByDescending(u => u.Ratings).ThenBy(u => u.UserId)
            .ToList();

        var heavy = users.Take(Heavy).ToList();
        var typicalStart = Math.Max(0, users.Count / 2 - Typical / 2);
        var typical = users.Skip(typicalStart).Take(Typical).ToList();
        return new TestInputs(movieIds, typical, heavy);
    }
}

/// <summary>Sends each kind of request to the running API many times, one after another, and times it.</summary>
public sealed class LatencyRunner(
    HttpClient http, MongoContext ctx, string demoPassword, int iterations, int warmup, double maxSeconds)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<LatencyResult>> RunAsync(TestInputs inputs)
    {
        var typical = new List<string>();
        foreach (var user in inputs.TypicalUsers) typical.Add(await LoginAsync($"user{user.UserId}", demoPassword));
        var heavy = new List<string>();
        foreach (var user in inputs.HeavyUsers) heavy.Add(await LoginAsync($"user{user.UserId}", demoPassword));

        var movies = inputs.MovieIds;
        var results = new List<LatencyResult>
        {
            await MeasureAsync("browse_popular", "Catalog page, most rated first",
                i => Get($"/api/movies?page={i % 5 + 1}")),
            await MeasureAsync("browse_genre_best_rated", "One genre, best rated first, at least 50 ratings",
                i => Get($"/api/movies?genre={Pick(TestInputs.Genres, i)}&sort=rating&minRatings=50")),
            await MeasureAsync("search_text", "Text search over titles and tags",
                i => Get($"/api/movies?q={Pick(TestInputs.SearchTerms, i)}")),
            await MeasureAsync("movie_details", "One movie by id",
                i => Get($"/api/movies/{Pick(movies, i)}")),
            await MeasureAsync("similar_movies", "Stored neighbors of one movie",
                i => Get($"/api/movies/{Pick(movies, i)}/similar")),
            await MeasureAsync("popular_in_genre", "Best-rated movies of one genre (weighted average)",
                i => Get($"/api/movies/popular?genre={Pick(TestInputs.Genres, i)}")),
            await MeasureAsync("user_history", "A typical user's latest ratings",
                i => Get("/api/users/me/history", Pick(typical, i))),
            await MeasureAsync("recommendations_typical", "Personal recommendations, user with a typical number of ratings",
                i => Get("/api/recommendations/me?limit=20", Pick(typical, i))),
            await MeasureAsync("recommendations_heavy", "Personal recommendations, the users with the most ratings",
                i => Get("/api/recommendations/me?limit=20", Pick(heavy, i))),
        };

        results.AddRange(await MeasureRatingWritesAsync(movies));
        return results;
    }

    /// <summary>
    /// Rating writes are measured with a throwaway account: it rates a different movie on every request
    /// and removes all those ratings again, so the dataset is the same after the run as before.
    /// </summary>
    private async Task<List<LatencyResult>> MeasureRatingWritesAsync(IReadOnlyList<int> movies)
    {
        var name = "perf_" + Guid.NewGuid().ToString("N")[..10];
        var register = await http.PostAsJsonAsync("/api/auth/register", new
        {
            username = name,
            email = $"{name}@cinematch.local",
            password = "Pf-" + Guid.NewGuid().ToString("N"),
        });
        register.EnsureSuccessStatusCode();
        var account = await register.Content.ReadFromJsonAsync<JsonElement>(Json);
        var token = account.GetProperty("token").GetString()!;
        var userId = account.GetProperty("user").GetProperty("id").GetInt32();

        try
        {
            var count = Math.Min(iterations, movies.Count - warmup);
            var add = await MeasureAsync("rating_add", "Rate a movie (rating + movie statistics in one transaction)",
                i => PutJson($"/api/movies/{movies[i]}/rating", new { rating = 4.0 }, token), count);

            // Remove exactly what was added, including the warm-up ratings.
            var remove = await MeasureAsync("rating_remove", "Remove a rating (transaction)",
                i => new HttpRequestMessage(HttpMethod.Delete, $"/api/movies/{movies[i]}/rating").WithToken(token), add.Requests);
            return [add, remove];
        }
        finally
        {
            // Whatever happened, leave no trace of the throwaway account.
            await ctx.Ratings.DeleteManyAsync(r => r.UserId == userId);
            await ctx.Users.DeleteOneAsync(u => u.Id == userId);
        }
    }

    private async Task<LatencyResult> MeasureAsync(
        string scenario, string description, Func<int, HttpRequestMessage> request, int? count = null)
    {
        var total = count ?? iterations;
        for (var i = 0; i < warmup; i++) await SendAsync(request(i));

        var samples = new List<double>(total);
        var errors = 0;
        string? note = null;
        var budget = Stopwatch.StartNew();
        for (var i = 0; i < total; i++)
        {
            if (budget.Elapsed.TotalSeconds > maxSeconds && samples.Count + errors >= 3)
            {
                note = $"stopped after {maxSeconds:0}s";
                break;
            }

            var (milliseconds, failure) = await SendAsync(request(warmup + i));
            if (failure is null)
            {
                samples.Add(milliseconds);
            }
            else
            {
                errors++;
                note ??= failure;
            }
        }

        var result = LatencyResult.From(scenario, description, samples, errors, note);
        Console.WriteLine(
            $"  {scenario,-26} p50 {result.P50Ms,9:F1} ms   p95 {result.P95Ms,9:F1} ms   " +
            $"({result.Requests} requests{(errors > 0 ? $", {errors} errors" : "")}{(note is null ? "" : $", {note}")})");
        return result;
    }

    /// <summary>Times one request up to the last byte of the response.</summary>
    private async Task<(double Milliseconds, string? Failure)> SendAsync(HttpRequestMessage request)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using (request)
            using (var response = await http.SendAsync(request))
            {
                await response.Content.ReadAsByteArrayAsync();
                var elapsed = watch.Elapsed.TotalMilliseconds;
                return (elapsed, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (watch.Elapsed.TotalMilliseconds, ex.GetType().Name);
        }
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Could not log in as {username} (HTTP {(int)response.StatusCode}). The imported accounts must use the " +
                "password stored in the importer's user secret Importer:DemoPassword.");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;
    }

    private static T Pick<T>(IReadOnlyList<T> values, int i) => values[i % values.Count];

    private static HttpRequestMessage Get(string path, string? token = null) =>
        new HttpRequestMessage(HttpMethod.Get, path).WithToken(token);

    private static HttpRequestMessage PutJson(string path, object body, string token) =>
        new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) }.WithToken(token);
}

internal static class HttpRequestExtensions
{
    public static HttpRequestMessage WithToken(this HttpRequestMessage request, string? token)
    {
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
