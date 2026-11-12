using System.Runtime.InteropServices;
using CineMatch.Importer;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Infrastructure.Repositories;
using CineMatch.Recommender;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Perf;

/// <summary>Everything the tool does to the database directly: describe it, load a dataset, drop or create indexes.</summary>
public sealed class Database(MongoContext ctx)
{
    private const double BytesPerMb = 1024 * 1024;

    public async Task<MachineInfo> DescribeMachineAsync()
    {
        var build = await ctx.Database.RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1));
        return new MachineInfo(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (BytesPerMb * 1024), 1),
            build["version"].AsString);
    }

    public async Task<DatasetInfo> DescribeDataAsync(string? datasetKey = null)
    {
        var ratings = await ctx.Ratings.EstimatedDocumentCountAsync();
        var stats = await ctx.Database.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
        return new DatasetInfo(
            datasetKey ?? GuessDatasetKey(ratings),
            await ctx.Movies.EstimatedDocumentCountAsync(),
            await ctx.Users.EstimatedDocumentCountAsync(),
            ratings,
            await ctx.ItemSimilarities.EstimatedDocumentCountAsync(),
            Math.Round(stats["dataSize"].ToDouble() / BytesPerMb, 1),
            Math.Round(stats["indexSize"].ToDouble() / BytesPerMb, 1));
    }

    /// <summary>Names the loaded dataset from its number of ratings.</summary>
    public static string GuessDatasetKey(long ratings) => ratings switch
    {
        < 500_000 => "small",
        < 5_000_000 => "1m",
        < 20_000_000 => "10m",
        _ => "32m",
    };

    /// <summary>Replaces the database content with one MovieLens dataset and builds its similarity table.</summary>
    public async Task<PrepareResult> PrepareAsync(
        Dataset dataset, string dataDirectory, string demoPassword, string adminPassword, RecommenderOptions options)
    {
        await dataset.EnsureDownloadedAsync(dataDirectory);

        var loader = new DatabaseLoader(ctx);
        await loader.ResetAsync();
        Console.WriteLine($"Importing '{dataset.Key}' ...");
        var import = await loader.LoadAsync(new MovieLensReader(dataset, dataDirectory), demoPassword, adminPassword);

        Console.WriteLine("Building the similarity table ...");
        var rebuilder = new SimilarityRebuilder(new RatingRepository(ctx), new SimilarityRepository(ctx), options);
        var similarity = await rebuilder.RunAsync()
                         ?? throw new InvalidOperationException("The similarity build did not run.");
        Console.WriteLine(
            $"Similarity table: {similarity.MoviesWithNeighbors:N0} movies with neighbors " +
            $"(load {similarity.LoadSeconds}s, build {similarity.BuildSeconds}s, save {similarity.SaveSeconds}s)");

        return new PrepareResult(
            dataset.Key, DateTime.UtcNow, import, similarity, await DescribeDataAsync(dataset.Key), await DescribeMachineAsync());
    }

    /// <summary>Drops every index except the built-in id indexes: the "no indexes" baseline.</summary>
    public async Task DropIndexesAsync()
    {
        await ctx.Users.Indexes.DropAllAsync();
        await ctx.Movies.Indexes.DropAllAsync();
        await ctx.Ratings.Indexes.DropAllAsync();
        await ctx.Watchlist.Indexes.DropAllAsync();
    }

    public Task CreateIndexesAsync() => new IndexInitializer(ctx).EnsureAsync();

    public async Task<Dictionary<string, List<string>>> ListIndexesAsync()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var name in new[] { MongoContext.UsersName, MongoContext.MoviesName, MongoContext.RatingsName, MongoContext.WatchlistName })
        {
            var indexes = await (await ctx.Database.GetCollection<BsonDocument>(name).Indexes.ListAsync()).ToListAsync();
            result[name] = indexes.Select(i => i["name"].AsString).Where(n => n != "_id_").Order(StringComparer.Ordinal).ToList();
        }
        return result;
    }
}
