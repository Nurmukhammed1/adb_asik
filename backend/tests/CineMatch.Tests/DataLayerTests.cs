using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CineMatch.Tests;

/// <summary>Checks the database rules that the API relies on, straight against MongoDB.</summary>
[Collection(ApiCollection.Name)]
public class DataLayerTests(ApiFixture api)
{
    [Fact]
    public async Task A_user_cannot_have_two_ratings_for_the_same_movie()
    {
        var movie = await api.NewMovieAsync();
        var (_, user) = await api.NewUserAsync();
        var rating = new Rating { UserId = user.Id, MovieId = movie.Id, Value = 4, Ts = DateTime.UtcNow };
        await api.Mongo.Ratings.InsertOneAsync(rating);

        var error = await Assert.ThrowsAsync<MongoWriteException>(() => api.Mongo.Ratings.InsertOneAsync(
            new Rating { UserId = user.Id, MovieId = movie.Id, Value = 2, Ts = DateTime.UtcNow }));

        Assert.Equal(ServerErrorCategory.DuplicateKey, error.WriteError.Category);
    }

    [Fact]
    public async Task Ratings_are_stored_under_the_field_name_rating()
    {
        var movie = await api.NewMovieAsync();
        var (client, user) = await api.NewUserAsync();
        await client.RateAsync(movie.Id, 3.5);

        var raw = await api.Mongo.Database.GetCollection<BsonDocument>(MongoContext.RatingsName)
            .Find(new BsonDocument { { "userId", user.Id }, { "movieId", movie.Id } })
            .SingleAsync();

        Assert.Equal(3.5, raw["rating"].AsDouble);
        Assert.True(raw["ts"].IsValidDateTime);
    }

    [Fact]
    public async Task Events_is_a_time_series_collection()
    {
        var collections = await (await api.Mongo.Database.ListCollectionsAsync(new ListCollectionsOptions
        {
            Filter = new BsonDocument("name", MongoContext.EventsName),
        })).ToListAsync();

        var events = Assert.Single(collections);
        Assert.Equal("timeseries", events["type"].AsString);
        Assert.Equal("ts", events["options"]["timeseries"]["timeField"].AsString);
        Assert.Equal("meta", events["options"]["timeseries"]["metaField"].AsString);
    }

    [Fact]
    public async Task Every_planned_index_exists()
    {
        Assert.Equal(
            ["_id_", "email_1", "username_1"],
            await IndexNamesAsync(api.Mongo.Users));
        Assert.Equal(
            [
                "_id_",
                "genres_1_ratingStats.count_-1__id_1",
                "ratingStats.avg_-1_ratingStats.count_-1__id_1",
                "ratingStats.count_-1__id_1",
                "text_title_tags",
                "year_-1_title_1__id_1",
            ],
            await IndexNamesAsync(api.Mongo.Movies));
        Assert.Equal(
            ["_id_", "movieId_1", "userId_1_movieId_1", "userId_1_ts_-1"],
            await IndexNamesAsync(api.Mongo.Ratings));
        Assert.Equal(
            ["_id_", "movieId_1", "userId_1_addedAt_-1", "userId_1_movieId_1"],
            await IndexNamesAsync(api.Mongo.Watchlist));
    }

    [Fact]
    public async Task Creating_the_indexes_again_changes_nothing()
    {
        var before = await IndexNamesAsync(api.Mongo.Movies);

        await new IndexInitializer(api.Mongo).EnsureAsync();

        Assert.Equal(before, await IndexNamesAsync(api.Mongo.Movies));
    }

    [Fact]
    public async Task Id_sequences_never_hand_out_the_same_number_twice()
    {
        var sequence = ApiFixture.Unique("seq");

        var ids = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => api.Mongo.NextIdAsync(sequence)));

        Assert.Equal(Enumerable.Range(1, 50), ids.Order());
    }

    [Fact]
    public async Task Raising_a_sequence_never_moves_it_backwards()
    {
        var sequence = ApiFixture.Unique("seq");

        await api.Mongo.SetSequenceAtLeastAsync(sequence, 500);
        await api.Mongo.SetSequenceAtLeastAsync(sequence, 100);

        Assert.Equal(501, await api.Mongo.NextIdAsync(sequence));
    }

    private static async Task<List<string>> IndexNamesAsync<T>(IMongoCollection<T> collection)
    {
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        return indexes.Select(i => i["name"].AsString).Order(StringComparer.Ordinal).ToList();
    }
}
