using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Repositories;

public class EventRepository(MongoContext ctx) : IEventRepository
{
    public Task AddAsync(int userId, int movieId, string type, CancellationToken ct = default) =>
        ctx.Events.InsertOneAsync(
            new InteractionEvent
            {
                Ts = DateTime.UtcNow,
                Meta = new EventMeta { UserId = userId, Type = type },
                MovieId = movieId,
            },
            cancellationToken: ct);

    public async Task<PagedResult<InteractionEvent>> GetByUserAsync(
        int userId, string type, Paging paging, CancellationToken ct = default)
    {
        var filter = Builders<InteractionEvent>.Filter.Where(e => e.Meta.UserId == userId && e.Meta.Type == type);

        var itemsTask = ctx.Events.Find(filter)
            .SortByDescending(e => e.Ts)
            .Skip(paging.Skip)
            .Limit(paging.PageSize)
            .ToListAsync(ct);
        var totalTask = ctx.Events.CountDocumentsAsync(filter, cancellationToken: ct);
        await Task.WhenAll(itemsTask, totalTask);

        return new PagedResult<InteractionEvent>(itemsTask.Result, totalTask.Result, paging.Page, paging.PageSize);
    }
}

public class WatchlistRepository(MongoContext ctx) : IWatchlistRepository
{
    public Task AddAsync(int userId, int movieId, CancellationToken ct = default) =>
        ctx.Watchlist.UpdateOneAsync(
            w => w.UserId == userId && w.MovieId == movieId,
            Builders<WatchlistItem>.Update.SetOnInsert(w => w.AddedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true },
            ct);

    public async Task<bool> RemoveAsync(int userId, int movieId, CancellationToken ct = default)
    {
        var result = await ctx.Watchlist.DeleteOneAsync(w => w.UserId == userId && w.MovieId == movieId, ct);
        return result.DeletedCount == 1;
    }

    public async Task<bool> ContainsAsync(int userId, int movieId, CancellationToken ct = default) =>
        await ctx.Watchlist.Find(w => w.UserId == userId && w.MovieId == movieId).Limit(1).CountDocumentsAsync(ct) > 0;

    public async Task<PagedResult<WatchlistItem>> GetByUserAsync(int userId, Paging paging, CancellationToken ct = default)
    {
        var itemsTask = ctx.Watchlist.Find(w => w.UserId == userId)
            .SortByDescending(w => w.AddedAt)
            .Skip(paging.Skip)
            .Limit(paging.PageSize)
            .ToListAsync(ct);
        var totalTask = ctx.Watchlist.CountDocumentsAsync(w => w.UserId == userId, cancellationToken: ct);
        await Task.WhenAll(itemsTask, totalTask);

        return new PagedResult<WatchlistItem>(itemsTask.Result, totalTask.Result, paging.Page, paging.PageSize);
    }
}
