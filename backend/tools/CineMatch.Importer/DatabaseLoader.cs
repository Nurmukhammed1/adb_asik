using System.Diagnostics;
using CineMatch.Core.Models;
using CineMatch.Infrastructure;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Importer;

/// <summary>What one import loaded and how long each part took.</summary>
public record ImportResult(
    int Movies,
    long Ratings,
    int Users,
    double MoviesSeconds,
    double RatingsSeconds,
    double UsersSeconds,
    double RatingStatsSeconds,
    double IndexSeconds,
    double TotalSeconds);

/// <summary>Loads a MovieLens dataset into MongoDB.</summary>
public class DatabaseLoader(MongoContext ctx)
{
    private const int BatchSize = 10_000;
    private const int ParallelInserts = 4;

    private static readonly InsertManyOptions Unordered = new() { IsOrdered = false };

    public async Task<bool> HasDataAsync() => await ctx.Movies.EstimatedDocumentCountAsync() > 0;

    public async Task ResetAsync()
    {
        string[] collections =
        [
            MongoContext.MoviesName, MongoContext.RatingsName, MongoContext.UsersName, MongoContext.EventsName,
            MongoContext.WatchlistName, MongoContext.ItemSimilaritiesName, MongoContext.CountersName,
        ];
        foreach (var name in collections) await ctx.Database.DropCollectionAsync(name);
        Console.WriteLine("Dropped all CineMatch collections.");
    }

    public async Task<ImportResult> LoadAsync(MovieLensReader reader, string demoPassword, string adminPassword)
    {
        var total = Stopwatch.StartNew();

        // 1. Movies
        var step = Stopwatch.StartNew();
        var movies = reader.ReadMovies();
        foreach (var batch in movies.Values.Chunk(BatchSize)) await ctx.Movies.InsertManyAsync(batch, Unordered);
        var moviesSeconds = step.Elapsed.TotalSeconds;
        Console.WriteLine($"Movies:  {movies.Count,12:N0}  ({moviesSeconds:F1}s)");

        // 2. Ratings: read as a stream, inserted in parallel batches. Indexes come afterwards, which is faster.
        step.Restart();
        var userIds = new HashSet<int>();
        long ratingCount = 0, inserted = 0;

        IEnumerable<Rating> Tracked()
        {
            foreach (var rating in reader.ReadRatings())
            {
                userIds.Add(rating.UserId);
                ratingCount++;
                yield return rating;
            }
        }

        await Parallel.ForEachAsync(
            Tracked().Chunk(BatchSize),
            new ParallelOptions { MaxDegreeOfParallelism = ParallelInserts },
            async (batch, ct) =>
            {
                await ctx.Ratings.InsertManyAsync(batch, Unordered, ct);
                var done = Interlocked.Add(ref inserted, batch.Length);
                if (done / 1_000_000 != (done - batch.Length) / 1_000_000)
                    Console.WriteLine($"  ... {done:N0} ratings");
            });
        var ratingsSeconds = step.Elapsed.TotalSeconds;
        Console.WriteLine($"Ratings: {ratingCount,12:N0}  ({ratingsSeconds:F1}s)");

        // 3. Demo accounts: one per MovieLens user, plus an admin.
        step.Restart();
        var hasher = new BcryptPasswordHasher();
        var demoHash = hasher.Hash(demoPassword);   // hashed once and shared; these are throwaway demo accounts
        var now = DateTime.UtcNow;
        var users = userIds.Order().Select(id => new User
        {
            Id = id,
            Username = $"user{id}",
            Email = $"user{id}@cinematch.local",
            PasswordHash = demoHash,
            Role = Roles.User,
            Profile = new UserProfile { DisplayName = $"User {id}" },
            CreatedAt = now,
        });
        foreach (var batch in users.Chunk(BatchSize)) await ctx.Users.InsertManyAsync(batch, Unordered);

        var adminId = (userIds.Count > 0 ? userIds.Max() : 0) + 1;
        await ctx.Users.InsertOneAsync(new User
        {
            Id = adminId,
            Username = "admin",
            Email = "admin@cinematch.local",
            PasswordHash = hasher.Hash(adminPassword),
            Role = Roles.Admin,
            Profile = new UserProfile { DisplayName = "Administrator" },
            CreatedAt = now,
        });
        var usersSeconds = step.Elapsed.TotalSeconds;
        Console.WriteLine($"Users:   {userIds.Count + 1,12:N0}  ({usersSeconds:F1}s)  user<id> accounts + admin");

        // New users and movies created through the API continue after the imported ids.
        await ctx.SetSequenceAtLeastAsync(MongoContext.UsersName, adminId);
        await ctx.SetSequenceAtLeastAsync(MongoContext.MoviesName, movies.Count > 0 ? movies.Keys.Max() : 0);

        // 4. Rating count, sum and average of every movie, computed inside the database.
        step.Restart();
        await ComputeRatingStatsAsync();
        var statsSeconds = step.Elapsed.TotalSeconds;
        Console.WriteLine($"Rating stats computed          ({statsSeconds:F1}s)");

        // 5. Indexes
        step.Restart();
        await new IndexInitializer(ctx).EnsureAsync();
        var indexSeconds = step.Elapsed.TotalSeconds;
        Console.WriteLine($"Indexes built                  ({indexSeconds:F1}s)");

        Console.WriteLine($"Import finished in {total.Elapsed.TotalSeconds:F1}s");
        return new ImportResult(
            movies.Count, ratingCount, userIds.Count + 1,
            Round(moviesSeconds), Round(ratingsSeconds), Round(usersSeconds), Round(statsSeconds), Round(indexSeconds),
            Round(total.Elapsed.TotalSeconds));

        static double Round(double seconds) => Math.Round(seconds, 2);
    }

    private Task ComputeRatingStatsAsync()
    {
        BsonDocument[] pipeline =
        [
            new("$group", new BsonDocument
            {
                { "_id", "$movieId" },
                { "count", new BsonDocument("$sum", 1) },
                { "sum", new BsonDocument("$sum", "$rating") },
            }),
            new("$project", new BsonDocument("ratingStats", new BsonDocument
            {
                { "count", "$count" },
                { "sum", "$sum" },
                { "avg", new BsonDocument("$divide", new BsonArray { "$sum", "$count" }) },
            })),
            new("$merge", new BsonDocument
            {
                { "into", MongoContext.MoviesName },
                { "on", "_id" },
                { "whenMatched", "merge" },
                { "whenNotMatched", "discard" },
            }),
        ];
        return ctx.Database.GetCollection<BsonDocument>(MongoContext.RatingsName)
            .AggregateToCollectionAsync<BsonDocument>(pipeline, new AggregateOptions { AllowDiskUse = true });
    }
}
