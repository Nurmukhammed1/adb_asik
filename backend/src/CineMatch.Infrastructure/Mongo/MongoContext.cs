using CineMatch.Core.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Mongo;

public class MongoSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?directConnection=true";
    public string Database { get; set; } = "cinematch";
}

/// <summary>The MongoDB connection and the typed collections of the platform.</summary>
public class MongoContext
{
    public const string UsersName = "users";
    public const string MoviesName = "movies";
    public const string RatingsName = "ratings";
    public const string EventsName = "events";
    public const string WatchlistName = "watchlist";
    public const string ItemSimilaritiesName = "item_similarities";
    public const string CountersName = "counters";

    private static readonly object MappingLock = new();
    private static bool _mappingsRegistered;

    public MongoContext(MongoSettings settings)
    {
        RegisterMappings();
        Client = new MongoClient(settings.ConnectionString);
        Database = Client.GetDatabase(settings.Database);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<User> Users => Database.GetCollection<User>(UsersName);
    public IMongoCollection<Movie> Movies => Database.GetCollection<Movie>(MoviesName);
    public IMongoCollection<Rating> Ratings => Database.GetCollection<Rating>(RatingsName);
    public IMongoCollection<InteractionEvent> Events => Database.GetCollection<InteractionEvent>(EventsName);
    public IMongoCollection<WatchlistItem> Watchlist => Database.GetCollection<WatchlistItem>(WatchlistName);
    public IMongoCollection<ItemSimilarity> ItemSimilarities => Database.GetCollection<ItemSimilarity>(ItemSimilaritiesName);
    public IMongoCollection<BsonDocument> Counters => Database.GetCollection<BsonDocument>(CountersName);

    /// <summary>Returns the next integer id of a sequence (atomic, safe under concurrency).</summary>
    public async Task<int> NextIdAsync(string sequence, CancellationToken ct = default)
    {
        var counter = await Counters.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", sequence),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);
        return counter["seq"].ToInt32();
    }

    /// <summary>Raises a sequence to at least <paramref name="value"/> (used after bulk imports).</summary>
    public Task SetSequenceAtLeastAsync(string sequence, int value, CancellationToken ct = default) =>
        Counters.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", sequence),
            Builders<BsonDocument>.Update.Max("seq", value),
            new UpdateOptions { IsUpsert = true },
            ct);

    public Task PingAsync(CancellationToken ct = default) =>
        Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);

    /// <summary>
    /// Maps the Core models to documents: camelCase field names, unknown fields ignored
    /// (so classes without an id property can still be read), nulls left out.
    /// </summary>
    private static void RegisterMappings()
    {
        lock (MappingLock)
        {
            if (_mappingsRegistered) return;

            var conventions = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new IgnoreIfNullConvention(true),
            };
            ConventionRegistry.Register("cinematch", conventions, t => t.Namespace == typeof(Movie).Namespace);

            BsonClassMap.RegisterClassMap<Rating>(map =>
            {
                map.AutoMap();
                map.MapMember(r => r.Value).SetElementName("rating");
            });

            _mappingsRegistered = true;
        }
    }
}
