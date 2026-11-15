using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Repositories;

public class SimilarityRepository(MongoContext ctx) : ISimilarityRepository
{
    private const string BuildingName = MongoContext.ItemSimilaritiesName + "_building";
    private const int BatchSize = 1_000;

    public async Task<ItemSimilarity?> GetAsync(int movieId, CancellationToken ct = default) =>
        await ctx.ItemSimilarities.Find(s => s.Id == movieId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ItemSimilarity>> GetManyAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct = default)
    {
        if (movieIds.Count == 0) return [];
        return await ctx.ItemSimilarities.Find(Builders<ItemSimilarity>.Filter.In(s => s.Id, movieIds)).ToListAsync(ct);
    }

    public async Task<long> ReplaceAllAsync(IEnumerable<ItemSimilarity> entries, CancellationToken ct = default)
    {
        // Write the new set next to the live one, then swap with a rename: requests keep reading
        // the old entries until the new ones are complete.
        await ctx.Database.DropCollectionAsync(BuildingName, ct);
        var building = ctx.Database.GetCollection<ItemSimilarity>(BuildingName);

        long count = 0;
        foreach (var batch in entries.Chunk(BatchSize))
        {
            await building.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
            count += batch.Length;
        }

        if (count == 0)
        {
            await ctx.Database.DropCollectionAsync(MongoContext.ItemSimilaritiesName, ct);
            return 0;
        }

        await ctx.Database.RenameCollectionAsync(
            BuildingName, MongoContext.ItemSimilaritiesName, new RenameCollectionOptions { DropTarget = true }, ct);
        return count;
    }

    public Task<long> CountAsync(CancellationToken ct = default) =>
        ctx.ItemSimilarities.EstimatedDocumentCountAsync(cancellationToken: ct);

    // Every entry of one rebuild carries the same time, so any one of them tells which set is live.
    public async Task<DateTime?> GetComputedAtAsync(CancellationToken ct = default)
    {
        var entry = await ctx.ItemSimilarities.Find(Builders<ItemSimilarity>.Filter.Empty)
            .Project(s => new { s.ComputedAt })
            .FirstOrDefaultAsync(ct);
        return entry?.ComputedAt;
    }
}
