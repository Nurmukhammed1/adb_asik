using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Driver;

namespace CineMatch.Importer;

/// <summary>Replaces stand-in descriptions with real ones and adds posters, using the TMDB API.</summary>
public class TmdbEnricher(MongoContext ctx, string accessToken)
{
    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w342";
    private const int ParallelRequests = 8;

    /// <summary>Enriches the <paramref name="limit"/> most-rated movies that have no poster yet.</summary>
    public async Task RunAsync(int limit)
    {
        var targets = await ctx.Movies
            .Find(m => m.TmdbId != null && m.PosterUrl == null)
            .SortByDescending(m => m.RatingStats.Count)
            .Limit(limit)
            .Project(m => new { m.Id, m.TmdbId })
            .ToListAsync();
        if (targets.Count == 0)
        {
            Console.WriteLine("TMDB: no movies left to enrich (this dataset may have no TMDB ids).");
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        int updated = 0, missing = 0;
        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelRequests },
            async (target, ct) =>
            {
                var details = await FetchAsync(http, target.TmdbId!.Value, ct);
                if (details is null || (string.IsNullOrWhiteSpace(details.Overview) && details.PosterPath is null))
                {
                    Interlocked.Increment(ref missing);
                    return;
                }

                var changes = new List<UpdateDefinition<Movie>>();
                if (!string.IsNullOrWhiteSpace(details.Overview))
                    changes.Add(Builders<Movie>.Update.Set(m => m.Description, details.Overview.Trim()));
                if (details.PosterPath is not null)
                    changes.Add(Builders<Movie>.Update.Set(m => m.PosterUrl, PosterBaseUrl + details.PosterPath));

                await ctx.Movies.UpdateOneAsync(m => m.Id == target.Id, Builders<Movie>.Update.Combine(changes), cancellationToken: ct);
                var done = Interlocked.Increment(ref updated);
                if (done % 500 == 0) Console.WriteLine($"  ... {done:N0} movies enriched");
            });

        Console.WriteLine($"TMDB: enriched {updated:N0} movies, {missing:N0} had no data.");
    }

    private static async Task<TmdbMovie?> FetchAsync(HttpClient http, int tmdbId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync($"movie/{tmdbId}", ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.Unauthorized:
                    throw new InvalidOperationException(
                        "TMDB rejected the token. Tmdb:AccessToken must be the long \"API Read Access Token\", not the short API key.");
                case HttpStatusCode.TooManyRequests when attempt <= 5:
                    await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2), ct);
                    continue;
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TmdbMovie>(ct);
        }
    }

    private sealed record TmdbMovie(
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("poster_path")] string? PosterPath);
}
