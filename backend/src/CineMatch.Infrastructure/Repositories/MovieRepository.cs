using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Repositories;

public class MovieRepository(MongoContext ctx) : IMovieRepository
{
    // List views fetch only these fields, not the description and tags.
    private static readonly ProjectionDefinition<Movie> SummaryProjection = Builders<Movie>.Projection
        .Include(m => m.Title)
        .Include(m => m.Year)
        .Include(m => m.Genres)
        .Include(m => m.PosterUrl)
        .Include(m => m.RatingStats);

    public async Task<PagedResult<MovieSummary>> SearchAsync(MovieQuery query, CancellationToken ct = default)
    {
        var paging = Paging.Normalize(query.Page, query.PageSize);
        var hasText = !string.IsNullOrWhiteSpace(query.Q);

        var f = Builders<Movie>.Filter;
        var filter = f.Empty;
        if (hasText) filter &= f.Text(query.Q!.Trim());
        if (!string.IsNullOrWhiteSpace(query.Genre)) filter &= f.AnyEq(m => m.Genres, query.Genre.Trim());
        if (query.YearFrom is { } from) filter &= f.Gte(m => m.Year, from);
        if (query.YearTo is { } to) filter &= f.Lte(m => m.Year, to);
        if (query.MinRatings is > 0) filter &= f.Gte(m => m.RatingStats.Count, query.MinRatings.Value);

        var s = Builders<Movie>.Sort;
        var popularity = s.Descending(m => m.RatingStats.Count);
        var sort = query.Sort switch
        {
            MovieSort.Rating => s.Descending(m => m.RatingStats.Avg).Descending(m => m.RatingStats.Count),
            MovieSort.Popularity => popularity,
            MovieSort.Year => s.Descending(m => m.Year).Ascending(m => m.Title),
            MovieSort.Title => s.Ascending(m => m.Title),
            _ => hasText ? s.MetaTextScore("score") : popularity,
        };
        // The id makes the order fully defined, so pages never overlap.
        sort = s.Combine(sort, s.Ascending(m => m.Id));

        var itemsTask = ctx.Movies.Find(filter)
            .Sort(sort)
            .Skip(paging.Skip)
            .Limit(paging.PageSize)
            .Project<MovieSummary>(SummaryProjection)
            .ToListAsync(ct);
        var totalTask = ctx.Movies.CountDocumentsAsync(filter, cancellationToken: ct);
        await Task.WhenAll(itemsTask, totalTask);

        return new PagedResult<MovieSummary>(itemsTask.Result, totalTask.Result, paging.Page, paging.PageSize);
    }

    public async Task<Movie?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await ctx.Movies.Find(m => m.Id == id).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default) =>
        await ctx.Movies.Find(m => m.Id == id).Limit(1).CountDocumentsAsync(ct) > 0;

    public async Task<IReadOnlyList<MovieSummary>> GetSummariesAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        var found = await ctx.Movies.Find(Builders<Movie>.Filter.In(m => m.Id, ids))
            .Project<MovieSummary>(SummaryProjection)
            .ToListAsync(ct);
        var byId = found.ToDictionary(m => m.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken ct = default)
    {
        // Read untyped: for movies with no genres MongoDB 8 adds an "undefined" entry to the distinct values.
        var cursor = await ctx.Movies.DistinctAsync<BsonValue>("genres", Builders<Movie>.Filter.Empty, cancellationToken: ct);
        var values = await cursor.ToListAsync(ct);
        return values.Where(v => v.IsString)
            .Select(v => v.AsString)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Movie> CreateAsync(Movie movie, CancellationToken ct = default)
    {
        movie.Id = await ctx.NextIdAsync(MongoContext.MoviesName, ct);
        movie.RatingStats = new RatingStats();
        await ctx.Movies.InsertOneAsync(movie, cancellationToken: ct);
        return movie;
    }

    public async Task<Movie?> UpdateAsync(Movie movie, CancellationToken ct = default)
    {
        var update = Builders<Movie>.Update
            .Set(m => m.Title, movie.Title)
            .Set(m => m.Year, movie.Year)
            .Set(m => m.Genres, movie.Genres)
            .Set(m => m.Description, movie.Description)
            .Set(m => m.Tags, movie.Tags)
            .Set(m => m.PosterUrl, movie.PosterUrl)
            .Set(m => m.TmdbId, movie.TmdbId);

        return await ctx.Movies.FindOneAndUpdateAsync<Movie, Movie>(
            m => m.Id == movie.Id,
            update,
            new FindOneAndUpdateOptions<Movie, Movie> { ReturnDocument = ReturnDocument.After },
            ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var deleted = await ctx.Movies.DeleteOneAsync(m => m.Id == id, ct);
        if (deleted.DeletedCount == 0) return false;

        // Clean up everything that points at the movie. View events stay as a historical log.
        await ctx.Ratings.DeleteManyAsync(r => r.MovieId == id, ct);
        await ctx.Watchlist.DeleteManyAsync(w => w.MovieId == id, ct);
        await ctx.ItemSimilarities.DeleteOneAsync(s => s.Id == id, ct);
        return true;
    }

    public async Task<double> GetGlobalMeanRatingAsync(CancellationToken ct = default)
    {
        var totals = await ctx.Movies.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "sum", new BsonDocument("$sum", "$ratingStats.sum") },
                { "count", new BsonDocument("$sum", "$ratingStats.count") },
            })
            .FirstOrDefaultAsync(ct);

        var count = totals?["count"].ToInt64() ?? 0;
        return count > 0 ? totals!["sum"].ToDouble() / count : 3.5;
    }

    public async Task<IReadOnlyList<ScoredMovie>> GetPopularAsync(
        IReadOnlyCollection<string>? genres, IReadOnlyCollection<int> excludeIds, int limit,
        double priorMean, int priorWeight, CancellationToken ct = default)
    {
        var f = Builders<Movie>.Filter;
        var filter = f.Gt(m => m.RatingStats.Count, 0);
        if (genres is { Count: > 0 }) filter &= f.AnyIn(m => m.Genres, genres);
        if (excludeIds.Count > 0) filter &= f.Nin(m => m.Id, excludeIds);

        // (sum + weight * mean) / (count + weight): the more ratings a movie has, the less the prior matters.
        var score = new BsonDocument("$divide", new BsonArray
        {
            new BsonDocument("$add", new BsonArray { "$ratingStats.sum", priorMean * priorWeight }),
            new BsonDocument("$add", new BsonArray { "$ratingStats.count", priorWeight }),
        });

        var rows = await ctx.Movies.Aggregate()
            .Match(filter)
            .AppendStage<BsonDocument>(new BsonDocument("$addFields", new BsonDocument("score", score)))
            .Sort(new BsonDocument { { "score", -1 }, { "_id", 1 } })
            .Limit(limit)
            .Project<BsonDocument>(new BsonDocument
            {
                { "title", 1 }, { "year", 1 }, { "genres", 1 }, { "posterUrl", 1 }, { "ratingStats", 1 }, { "score", 1 },
            })
            .ToListAsync(ct);

        return rows
            .Select(row => new ScoredMovie(BsonSerializer.Deserialize<MovieSummary>(row), row["score"].ToDouble()))
            .ToList();
    }
}
