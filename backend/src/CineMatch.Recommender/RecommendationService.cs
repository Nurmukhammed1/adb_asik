using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;

namespace CineMatch.Recommender;

public static class RecommendationSources
{
    /// <summary>Personal: computed from the user's own ratings and the similarity table.</summary>
    public const string Collaborative = "collaborative";

    /// <summary>Well-rated movies with many ratings; used for new users and to fill short lists.</summary>
    public const string Popular = "popular";

    /// <summary>A stored neighbor of one specific movie.</summary>
    public const string Similar = "similar";
}

/// <param name="Source">See <see cref="RecommendationSources"/>.</param>
/// <param name="Score">
/// Depends on the source: the predicted rating (collaborative), the weighted average rating (popular)
/// or the similarity from 0 to 1 (similar).
/// </param>
/// <param name="BecauseOf">For collaborative items: the movie the user rated that speaks most for this one.</param>
public record Recommendation(MovieSummary Movie, string Source, double Score, MovieSummary? BecauseOf = null);

/// <param name="Strategy">"collaborative" if at least one item is personal, otherwise "popular".</param>
public record RecommendationList(string Strategy, IReadOnlyList<Recommendation> Items);

/// <summary>Builds the recommendation lists the API returns.</summary>
/// <remarks>
/// Finished personal and popular lists are kept in the cache. Every cache key contains the version of the
/// similarity table (the time it was computed), so after a rebuild the old lists are simply never read again.
/// A user's own lists are dropped as soon as their ratings or favorite genres change.
/// </remarks>
public sealed class RecommendationService(
    IRatingRepository ratings,
    ISimilarityRepository similarities,
    IMovieRepository movies,
    IUserRepository users,
    ICacheStore cache,
    RecommenderOptions options)
{
    public const int MaxLimit = 100;

    public static readonly TimeSpan PersonalCacheLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PopularCacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan GlobalMeanLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TableVersionLifetime = TimeSpan.FromSeconds(30);
    private (double Value, DateTime LoadedAt)? _globalMean;
    private TableVersion? _tableVersion;

    private sealed record TableVersion(string Value, DateTime LoadedAt);

    /// <summary>
    /// Personal recommendations. A user with too few ratings gets popular movies from their favorite
    /// genres; a list that comes out short is filled up with popular movies.
    /// </summary>
    public async Task<RecommendationList> ForUserAsync(int userId, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var key = UserKey(await TableVersionAsync(ct), userId);
        var field = limit.ToString();

        var cached = await cache.GetAsync<RecommendationList>(key, field);
        if (cached is not null) return cached;

        var list = await ComputeForUserAsync(userId, limit, ct);
        await cache.SetAsync(key, field, list, PersonalCacheLifetime);
        return list;
    }

    /// <summary>Drops the user's cached lists; call it whenever their ratings or favorite genres change.</summary>
    public async Task ForgetUserAsync(int userId, CancellationToken ct = default) =>
        await cache.RemoveAsync(UserKey(await TableVersionAsync(ct), userId));

    private async Task<RecommendationList> ComputeForUserAsync(int userId, int limit, CancellationToken ct)
    {
        var userRatings = await ratings.GetAllByUserAsync(userId, ct);
        var items = new List<Recommendation>(limit);

        if (userRatings.Count >= options.MinRatingsForPersonal)
        {
            // Two indexed queries: the user's ratings above, and the neighbor lists of exactly those movies.
            var stored = await similarities.GetManyAsync(userRatings.Select(r => r.MovieId).ToList(), ct);
            var neighborsOf = stored.ToDictionary(s => s.Id, s => s.Neighbors);

            // Ask for a few extra, in case a neighbor list still names a movie that was deleted since.
            var candidates = ItemBasedScorer.Recommend(
                userRatings, id => neighborsOf.GetValueOrDefault(id), options.RankBy, options.PredictionDamping, limit + 10);

            var needed = candidates.Select(c => c.MovieId)
                .Concat(candidates.Where(c => c.BecauseOf.HasValue).Select(c => c.BecauseOf!.Value))
                .Distinct()
                .ToList();
            var summaries = (await movies.GetSummariesAsync(needed, ct)).ToDictionary(m => m.Id);

            items.AddRange(candidates
                .Where(c => summaries.ContainsKey(c.MovieId))
                .Take(limit)
                .Select(c => new Recommendation(
                    summaries[c.MovieId],
                    RecommendationSources.Collaborative,
                    Math.Round(c.PredictedRating, 2),
                    c.BecauseOf is { } source ? summaries.GetValueOrDefault(source) : null)));
        }

        var strategy = items.Count > 0 ? RecommendationSources.Collaborative : RecommendationSources.Popular;
        if (items.Count < limit)
        {
            var exclude = userRatings.Select(r => r.MovieId).Concat(items.Select(i => i.Movie.Id)).ToHashSet();
            var favoriteGenres = (await users.GetByIdAsync(userId, ct))?.Profile.FavoriteGenres;

            if (favoriteGenres is { Count: > 0 })
                await FillWithPopularAsync(items, favoriteGenres, exclude, limit, ct);
            if (items.Count < limit)
                await FillWithPopularAsync(items, null, exclude, limit, ct);
        }

        return new RecommendationList(strategy, items);
    }

    /// <summary>The stored neighbors of a movie. Returns null if the movie does not exist.</summary>
    public async Task<IReadOnlyList<Recommendation>?> SimilarToAsync(int movieId, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var movie = await movies.GetByIdAsync(movieId, ct);
        if (movie is null) return null;

        var items = new List<Recommendation>(limit);
        var stored = await similarities.GetAsync(movieId, ct);
        if (stored is not null)
        {
            var summaries = (await movies.GetSummariesAsync(stored.Neighbors.Select(n => n.MovieId).ToList(), ct))
                .ToDictionary(m => m.Id);
            items.AddRange(stored.Neighbors
                .Where(n => summaries.ContainsKey(n.MovieId))
                .Take(limit)
                .Select(n => new Recommendation(summaries[n.MovieId], RecommendationSources.Similar, n.Score)));
        }

        // A movie with too few ratings has no neighbors yet: show popular movies of its genres instead.
        if (items.Count == 0)
        {
            var exclude = new HashSet<int> { movieId };
            await FillWithPopularAsync(items, movie.Genres, exclude, limit, ct);
        }
        return items;
    }

    public async Task<IReadOnlyList<Recommendation>> PopularAsync(string? genre, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var genres = string.IsNullOrWhiteSpace(genre) ? null : new[] { genre.Trim() };
        var key = PopularKey(await TableVersionAsync(ct));
        var field = $"{genres?[0] ?? "*"}:{limit}";

        var cached = await cache.GetAsync<List<Recommendation>>(key, field);
        if (cached is not null) return cached;

        var items = new List<Recommendation>();
        await FillWithPopularAsync(items, genres, [], limit, ct);
        await cache.SetAsync(key, field, items, PopularCacheLifetime);
        return items;
    }

    private static string UserKey(string tableVersion, int userId) => $"cinematch:rec:{tableVersion}:user:{userId}";

    private static string PopularKey(string tableVersion) => $"cinematch:rec:{tableVersion}:popular";

    // Which similarity table is live. Read from the database at most every 30 seconds, so a rebuild
    // (in the API or by the import and benchmark tools) retires the old cached lists within that time.
    private async Task<string> TableVersionAsync(CancellationToken ct)
    {
        if (_tableVersion is { } known && DateTime.UtcNow - known.LoadedAt < TableVersionLifetime) return known.Value;

        var computedAt = await similarities.GetComputedAtAsync(ct);
        var value = computedAt is { } at ? at.Ticks.ToString() : "empty";
        _tableVersion = new TableVersion(value, DateTime.UtcNow);
        return value;
    }

    private async Task FillWithPopularAsync(
        List<Recommendation> items, IReadOnlyCollection<string>? genres, HashSet<int> exclude, int limit, CancellationToken ct)
    {
        var popular = await movies.GetPopularAsync(
            genres, exclude, limit - items.Count, await GlobalMeanAsync(ct), options.PopularPriorWeight, ct);
        foreach (var entry in popular)
        {
            items.Add(new Recommendation(entry.Movie, RecommendationSources.Popular, Math.Round(entry.Score, 2)));
            exclude.Add(entry.Movie.Id);
        }
    }

    // The catalog-wide average moves very slowly, so it is read from the database only now and then.
    private async Task<double> GlobalMeanAsync(CancellationToken ct)
    {
        if (_globalMean is { } cached && DateTime.UtcNow - cached.LoadedAt < GlobalMeanLifetime) return cached.Value;

        var value = await movies.GetGlobalMeanRatingAsync(ct);
        _globalMean = (value, DateTime.UtcNow);
        return value;
    }
}
