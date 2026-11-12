using CineMatch.Core.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Infrastructure.Mongo;

/// <summary>Creates the time-series collection and all indexes. Safe to run on every start.</summary>
public class IndexInitializer(MongoContext ctx)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        var existing = await (await ctx.Database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!existing.Contains(MongoContext.EventsName))
        {
            await ctx.Database.CreateCollectionAsync(
                MongoContext.EventsName,
                new CreateCollectionOptions
                {
                    TimeSeriesOptions = new TimeSeriesOptions("ts", "meta", TimeSeriesGranularity.Seconds),
                },
                ct);
        }

        var unique = new CreateIndexOptions { Unique = true };

        var users = Builders<User>.IndexKeys;
        await ctx.Users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<User>(users.Ascending(u => u.Email), unique),
            new CreateIndexModel<User>(users.Ascending(u => u.Username), unique),
        ], ct);

        var movies = Builders<Movie>.IndexKeys;
        await ctx.Movies.Indexes.CreateManyAsync(
        [
            // Search by words in the title (weighted higher) and in the tags.
            new CreateIndexModel<Movie>(
                movies.Text(m => m.Title).Text(m => m.Tags),
                new CreateIndexOptions
                {
                    Name = "text_title_tags",
                    Weights = new BsonDocument { { "title", 10 }, { "tags", 2 } },
                }),
            // Browse a genre, most popular first.
            new CreateIndexModel<Movie>(
                movies.Ascending(m => m.Genres).Descending(m => m.RatingStats.Count).Ascending(m => m.Id)),
            // Whole catalog, most popular first.
            new CreateIndexModel<Movie>(
                movies.Descending(m => m.RatingStats.Count).Ascending(m => m.Id)),
            // Whole catalog, best rated first.
            new CreateIndexModel<Movie>(
                movies.Descending(m => m.RatingStats.Avg).Descending(m => m.RatingStats.Count).Ascending(m => m.Id)),
            // Newest first, and year-range filters.
            new CreateIndexModel<Movie>(
                movies.Descending(m => m.Year).Ascending(m => m.Title).Ascending(m => m.Id)),
        ], ct);

        var ratings = Builders<Rating>.IndexKeys;
        await ctx.Ratings.Indexes.CreateManyAsync(
        [
            // One rating per user per movie; also the lookup for "my rating of this movie".
            new CreateIndexModel<Rating>(ratings.Ascending(r => r.UserId).Ascending(r => r.MovieId), unique),
            // All ratings of a movie (recommender, cascade delete).
            new CreateIndexModel<Rating>(ratings.Ascending(r => r.MovieId)),
            // A user's history, newest first.
            new CreateIndexModel<Rating>(ratings.Ascending(r => r.UserId).Descending(r => r.Ts)),
        ], ct);

        var watchlist = Builders<WatchlistItem>.IndexKeys;
        await ctx.Watchlist.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<WatchlistItem>(watchlist.Ascending(w => w.UserId).Ascending(w => w.MovieId), unique),
            new CreateIndexModel<WatchlistItem>(watchlist.Ascending(w => w.UserId).Descending(w => w.AddedAt)),
            new CreateIndexModel<WatchlistItem>(watchlist.Ascending(w => w.MovieId)),
        ], ct);
    }
}
