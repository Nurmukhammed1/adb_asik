using System.Runtime.CompilerServices;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Repositories;

public class RatingRepository(MongoContext ctx) : IRatingRepository
{
    // The recommender needs only who rated what and how; skipping the id and timestamp halves the data sent.
    private static readonly ProjectionDefinition<Rating> TripleProjection = Builders<Rating>.Projection
        .Include(r => r.UserId)
        .Include(r => r.MovieId)
        .Include(r => r.Value)
        .Exclude("_id");

    public async Task<IReadOnlyList<Rating>> GetAllByUserAsync(int userId, CancellationToken ct = default) =>
        await ctx.Ratings.Find(r => r.UserId == userId).Project<Rating>(TripleProjection).ToListAsync(ct);

    public async IAsyncEnumerable<Rating> StreamAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cursor = await ctx.Ratings
            .Find(Builders<Rating>.Filter.Empty, new FindOptions { BatchSize = 20_000 })
            .Project<Rating>(TripleProjection)
            .ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var rating in cursor.Current) yield return rating;
        }
    }

    public Task<long> EstimateCountAsync(CancellationToken ct = default) =>
        ctx.Ratings.EstimatedDocumentCountAsync(cancellationToken: ct);

    public async Task<Rating?> GetAsync(int userId, int movieId, CancellationToken ct = default) =>
        await ctx.Ratings.Find(r => r.UserId == userId && r.MovieId == movieId).FirstOrDefaultAsync(ct);

    public async Task<RatingStats?> SetAsync(int userId, int movieId, double value, CancellationToken ct = default)
    {
        using var session = await ctx.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            // WithTransaction retries the whole block if two writers collide.
            return await session.WithTransactionAsync(async (s, token) =>
            {
                var previous = await ctx.Ratings.FindOneAndUpdateAsync<Rating, Rating>(
                    s,
                    r => r.UserId == userId && r.MovieId == movieId,
                    Builders<Rating>.Update.Set(r => r.Value, value).Set(r => r.Ts, DateTime.UtcNow),
                    new FindOneAndUpdateOptions<Rating, Rating> { IsUpsert = true, ReturnDocument = ReturnDocument.Before },
                    token);

                var countChange = previous is null ? 1 : 0;
                var sumChange = value - (previous?.Value ?? 0);
                var stats = await ApplyStatsChangeAsync(s, movieId, countChange, sumChange, token);

                // Throwing rolls the rating back, so a missing movie never keeps a rating.
                return stats ?? throw new MovieNotFoundException();
            }, cancellationToken: ct);
        }
        catch (MovieNotFoundException)
        {
            return null;
        }
    }

    public async Task<RatingStats?> RemoveAsync(int userId, int movieId, CancellationToken ct = default)
    {
        using var session = await ctx.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var removed = await ctx.Ratings.FindOneAndDeleteAsync<Rating>(
                s,
                r => r.UserId == userId && r.MovieId == movieId,
                cancellationToken: token);
            if (removed is null) return null;

            return await ApplyStatsChangeAsync(s, movieId, -1, -removed.Value, token) ?? new RatingStats();
        }, cancellationToken: ct);
    }

    public async Task<PagedResult<Rating>> GetByUserAsync(int userId, Paging paging, CancellationToken ct = default)
    {
        var itemsTask = ctx.Ratings.Find(r => r.UserId == userId)
            .SortByDescending(r => r.Ts)
            .Skip(paging.Skip)
            .Limit(paging.PageSize)
            .ToListAsync(ct);
        var totalTask = ctx.Ratings.CountDocumentsAsync(r => r.UserId == userId, cancellationToken: ct);
        await Task.WhenAll(itemsTask, totalTask);

        return new PagedResult<Rating>(itemsTask.Result, totalTask.Result, paging.Page, paging.PageSize);
    }

    /// <summary>
    /// Adds the changes to the movie's rating count and sum and recalculates the average,
    /// all inside the server in one update. Returns null if the movie does not exist.
    /// </summary>
    private async Task<RatingStats?> ApplyStatsChangeAsync(
        IClientSessionHandle session, int movieId, int countChange, double sumChange, CancellationToken ct)
    {
        var addChanges = new BsonDocument("$set", new BsonDocument
        {
            { "ratingStats.count", new BsonDocument("$add", new BsonArray { "$ratingStats.count", countChange }) },
            { "ratingStats.sum", new BsonDocument("$add", new BsonArray { "$ratingStats.sum", sumChange }) },
        });
        var recalculateAverage = new BsonDocument("$set", new BsonDocument(
            "ratingStats.avg",
            new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$gt", new BsonArray { "$ratingStats.count", 0 }),
                new BsonDocument("$divide", new BsonArray { "$ratingStats.sum", "$ratingStats.count" }),
                0.0,
            })));
        var update = Builders<Movie>.Update.Pipeline(
            PipelineDefinition<Movie, Movie>.Create([addChanges, recalculateAverage]));

        var movie = await ctx.Movies.FindOneAndUpdateAsync<Movie, Movie>(
            session,
            m => m.Id == movieId,
            update,
            new FindOneAndUpdateOptions<Movie, Movie>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = Builders<Movie>.Projection.Include(m => m.RatingStats),
            },
            ct);
        return movie?.RatingStats;
    }

    private sealed class MovieNotFoundException : Exception;
}
