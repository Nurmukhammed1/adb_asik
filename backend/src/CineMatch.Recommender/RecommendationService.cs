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
public sealed class RecommendationService(
    IRatingRepository ratings,
    ISimilarityRepository similarities,
    IMovieRepository movies,
    IUserRepository users,
    RecommenderOptions options)
{
    public const int MaxLimit = 100;

    private static readonly TimeSpan GlobalMeanLifetime = TimeSpan.FromMinutes(10);
    private (double Value, DateTime LoadedAt)? _globalMean;

    /// <summary>
    /// Personal recommendations. A user with too few ratings gets popular movies from their favorite
    /// genres; a list that comes out short is filled up with popular movies.
    /// </summary>
    public async Task<RecommendationList> ForUserAsync(int userId, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
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
        var items = new List<Recommendation>();
        var genres = string.IsNullOrWhiteSpace(genre) ? null : new[] { genre.Trim() };
        await FillWithPopularAsync(items, genres, [], Math.Clamp(limit, 1, MaxLimit), ct);
        return items;
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
