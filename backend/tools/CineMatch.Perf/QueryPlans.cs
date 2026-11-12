using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Perf;

/// <summary>
/// Asks MongoDB how it executes the API's main queries (explain with execution statistics):
/// how many documents and index entries it reads to return how many, and through which index.
/// </summary>
public sealed class QueryPlans(MongoContext ctx)
{
    public async Task<List<QueryPlan>> CaptureAsync(TestInputs inputs)
    {
        var movieId = inputs.MovieIds.Count > 0 ? inputs.MovieIds[0] : 1;
        var typicalUser = inputs.TypicalUsers.Count > 0 ? inputs.TypicalUsers[inputs.TypicalUsers.Count / 2].UserId : 1;
        var heavyUser = inputs.HeavyUsers.Count > 0 ? inputs.HeavyUsers[0].UserId : 1;
        var ratedByTypical = await ctx.Ratings.Find(r => r.UserId == typicalUser).Project(r => r.MovieId).ToListAsync();

        var byPopularity = new BsonDocument { { "ratingStats.count", -1 }, { "_id", 1 } };
        var byRating = new BsonDocument { { "ratingStats.avg", -1 }, { "ratingStats.count", -1 }, { "_id", 1 } };
        var byTextScore = new BsonDocument { { "score", new BsonDocument("$meta", "textScore") }, { "_id", 1 } };

        return
        [
            await FindAsync("Catalog page, most rated first", MongoContext.MoviesName,
                [], byPopularity, 20),
            await FindAsync("Genre page, best rated first (50+ ratings)", MongoContext.MoviesName,
                new BsonDocument { { "genres", "Comedy" }, { "ratingStats.count", new BsonDocument("$gte", 50) } }, byRating, 20),
            await FindAsync("Text search for \"star\"", MongoContext.MoviesName,
                new BsonDocument("$text", new BsonDocument("$search", "star")), byTextScore, 20),
            await FindAsync("One movie by id", MongoContext.MoviesName,
                new BsonDocument("_id", movieId), null, 1),
            await FindAsync("One user's rating of one movie", MongoContext.RatingsName,
                new BsonDocument { { "userId", typicalUser }, { "movieId", movieId } }, null, 1),
            await FindAsync("A typical user's history page", MongoContext.RatingsName,
                new BsonDocument("userId", typicalUser), new BsonDocument("ts", -1), 20),
            await FindAsync("All ratings of a typical user (recommendation input)", MongoContext.RatingsName,
                new BsonDocument("userId", typicalUser), null, null),
            await FindAsync("All ratings of the heaviest user (recommendation input)", MongoContext.RatingsName,
                new BsonDocument("userId", heavyUser), null, null),
            await FindAsync("Neighbor lists of a typical user's rated movies", MongoContext.ItemSimilaritiesName,
                new BsonDocument("_id", new BsonDocument("$in", new BsonArray(ratedByTypical))), null, null),
            await AggregateAsync("Popular movies of one genre (weighted average)", MongoContext.MoviesName,
            [
                new BsonDocument("$match", new BsonDocument
                {
                    { "ratingStats.count", new BsonDocument("$gt", 0) },
                    { "genres", new BsonDocument("$in", new BsonArray { "Comedy" }) },
                }),
                new BsonDocument("$addFields", new BsonDocument("score", new BsonDocument("$divide", new BsonArray
                {
                    new BsonDocument("$add", new BsonArray { "$ratingStats.sum", 175.0 }),
                    new BsonDocument("$add", new BsonArray { "$ratingStats.count", 50 }),
                }))),
                new BsonDocument("$sort", new BsonDocument { { "score", -1 }, { "_id", 1 } }),
                new BsonDocument("$limit", 20),
            ]),
        ];
    }

    private Task<QueryPlan> FindAsync(string query, string collection, BsonDocument filter, BsonDocument? sort, int? limit)
    {
        var find = new BsonDocument { { "find", collection }, { "filter", filter } };
        if (sort is not null) find["sort"] = sort;
        if (limit is not null) find["limit"] = limit.Value;
        return ExplainAsync(query, collection, find);
    }

    private Task<QueryPlan> AggregateAsync(string query, string collection, BsonDocument[] pipeline) =>
        ExplainAsync(query, collection, new BsonDocument
        {
            { "aggregate", collection },
            { "pipeline", new BsonArray(pipeline) },
            { "cursor", new BsonDocument() },
        });

    private async Task<QueryPlan> ExplainAsync(string query, string collection, BsonDocument command)
    {
        try
        {
            var explained = await ctx.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument { { "explain", command }, { "verbosity", "executionStats" } });
            return Parse(query, collection, explained);
        }
        catch (MongoCommandException ex)
        {
            // For example a text search while the text index does not exist.
            return new QueryPlan(query, collection, 0, 0, 0, 0, "FAILED: " + ex.Message.Split('\n')[0]);
        }
    }

    /// <summary>
    /// Pulls the numbers and the stage chain out of an explain result. The layout differs between plain
    /// finds, aggregations and query-engine versions, so the parts are searched for by name.
    /// </summary>
    public static QueryPlan Parse(string query, string collection, BsonDocument explained)
    {
        var stats = FindDocument(explained, "executionStats");
        var winning = FindDocument(explained, "winningPlan");

        var stages = new List<string>();
        if (winning is not null) CollectStages(winning, stages);
        if (explained.Contains("stages"))   // aggregation stages that run after the cursor, such as $sort
        {
            var later = explained["stages"].AsBsonArray
                .Select(s => s.AsBsonDocument.Names.First())
                .Where(n => n != "$cursor")
                .Select(n => n.TrimStart('$').ToUpperInvariant());
            stages.InsertRange(0, later.Reverse());
        }

        return new QueryPlan(
            query,
            collection,
            Number(stats, "totalDocsExamined"),
            Number(stats, "totalKeysExamined"),
            Number(stats, "nReturned"),
            Number(stats, "executionTimeMillis"),
            stages.Count > 0 ? string.Join(" > ", stages) : "unknown");
    }

    private static long Number(BsonDocument? document, string name) =>
        document is not null && document.TryGetValue(name, out var value) && value.IsNumeric ? value.ToInt64() : 0;

    /// <summary>The first sub-document with the given name, searching depth-first.</summary>
    private static BsonDocument? FindDocument(BsonValue value, string name)
    {
        switch (value)
        {
            case BsonDocument document:
                if (document.TryGetValue(name, out var found) && found.IsBsonDocument) return found.AsBsonDocument;
                foreach (var element in document)
                {
                    if (FindDocument(element.Value, name) is { } nested) return nested;
                }
                return null;
            case BsonArray array:
                foreach (var item in array)
                {
                    if (FindDocument(item, name) is { } nested) return nested;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>Lists the plan's stages from the outside in, with the index name where one is used.</summary>
    private static void CollectStages(BsonValue value, List<string> stages)
    {
        switch (value)
        {
            case BsonDocument document:
                if (document.TryGetValue("stage", out var stage) && stage.IsString)
                {
                    var name = stage.AsString;
                    if (document.TryGetValue("indexName", out var index)) name += $"({index.AsString})";
                    if (stages.Count == 0 || stages[^1] != name) stages.Add(name);
                }
                foreach (var element in document)
                {
                    // The slot-based plan is the same plan in the engine's internal notation.
                    if (element.Name is "slotBasedPlan" or "rejectedPlans") continue;
                    CollectStages(element.Value, stages);
                }
                break;
            case BsonArray array:
                foreach (var item in array) CollectStages(item, stages);
                break;
        }
    }
}
