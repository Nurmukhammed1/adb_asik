using System.Runtime.InteropServices;
using CineMatch.Core.Models;

namespace CineMatch.Recommender;

/// <summary>
/// All ratings in memory, in two compact layouts: grouped by user and grouped by movie.
/// Users and movies are numbered 0..n-1 internally; ratings are stored "centered", that is
/// minus the user's own average, which removes the difference between strict and generous raters.
/// About 16 bytes per rating in total.
/// </summary>
public sealed class RatingMatrix
{
    private RatingMatrix() { }

    /// <summary>Internal user number to user id.</summary>
    public int[] UserIds { get; private init; } = [];

    /// <summary>Internal movie number to movie id.</summary>
    public int[] MovieIds { get; private init; } = [];

    /// <summary>Each user's average rating.</summary>
    public float[] UserMeans { get; private init; } = [];

    // Ratings grouped by user: user u owns positions UserStart[u] .. UserStart[u + 1] - 1.
    public int[] UserStart { get; private init; } = [];
    public int[] UserItems { get; private init; } = [];
    public float[] UserValues { get; private init; } = [];

    // The same ratings grouped by movie.
    public int[] ItemStart { get; private init; } = [];
    public int[] ItemUsers { get; private init; } = [];
    public float[] ItemValues { get; private init; } = [];

    public int UserCount => UserIds.Length;
    public int ItemCount => MovieIds.Length;
    public int RatingCount => UserItems.Length;

    public static RatingMatrix From(IEnumerable<Rating> ratings)
    {
        var builder = new Builder();
        foreach (var rating in ratings) builder.Add(rating.UserId, rating.MovieId, rating.Value);
        return builder.Build();
    }

    /// <summary>One user's ratings with their original values.</summary>
    public List<Rating> RatingsOfUser(int user)
    {
        var ratings = new List<Rating>(UserStart[user + 1] - UserStart[user]);
        for (var p = UserStart[user]; p < UserStart[user + 1]; p++)
        {
            ratings.Add(new Rating
            {
                UserId = UserIds[user],
                MovieId = MovieIds[UserItems[p]],
                Value = UserValues[p] + UserMeans[user],
            });
        }
        return ratings;
    }

    /// <summary>Collects ratings one by one (for example from a database cursor), then builds the matrix.</summary>
    public sealed class Builder(int expectedRatings = 0)
    {
        private readonly List<int> _users = new(expectedRatings);
        private readonly List<int> _movies = new(expectedRatings);
        private readonly List<float> _values = new(expectedRatings);

        public void Add(int userId, int movieId, double rating)
        {
            _users.Add(userId);
            _movies.Add(movieId);
            _values.Add((float)rating);
        }

        public RatingMatrix Build()
        {
            var users = CollectionsMarshal.AsSpan(_users);
            var movies = CollectionsMarshal.AsSpan(_movies);
            var values = CollectionsMarshal.AsSpan(_values);
            var count = users.Length;

            // Replace ids by dense numbers in place, to avoid a second copy of everything.
            var userIds = Renumber(users);
            var movieIds = Renumber(movies);

            var userStart = new int[userIds.Length + 1];
            var itemStart = new int[movieIds.Length + 1];
            var sums = new double[userIds.Length];
            for (var k = 0; k < count; k++)
            {
                userStart[users[k] + 1]++;
                itemStart[movies[k] + 1]++;
                sums[users[k]] += values[k];
            }

            var means = new float[userIds.Length];
            for (var u = 0; u < means.Length; u++) means[u] = (float)(sums[u] / userStart[u + 1]);
            for (var u = 0; u < userIds.Length; u++) userStart[u + 1] += userStart[u];
            for (var i = 0; i < movieIds.Length; i++) itemStart[i + 1] += itemStart[i];

            var userItems = new int[count];
            var userValues = new float[count];
            var itemUsers = new int[count];
            var itemValues = new float[count];
            var nextOfUser = userStart.AsSpan(0, userIds.Length).ToArray();
            var nextOfItem = itemStart.AsSpan(0, movieIds.Length).ToArray();
            for (var k = 0; k < count; k++)
            {
                int u = users[k], i = movies[k];
                var centered = values[k] - means[u];

                var p = nextOfUser[u]++;
                userItems[p] = i;
                userValues[p] = centered;

                var q = nextOfItem[i]++;
                itemUsers[q] = u;
                itemValues[q] = centered;
            }

            return new RatingMatrix
            {
                UserIds = userIds,
                MovieIds = movieIds,
                UserMeans = means,
                UserStart = userStart,
                UserItems = userItems,
                UserValues = userValues,
                ItemStart = itemStart,
                ItemUsers = itemUsers,
                ItemValues = itemValues,
            };
        }

        /// <summary>Replaces each id by its rank among the distinct ids and returns the distinct ids in order.</summary>
        private static int[] Renumber(Span<int> ids)
        {
            var distinct = new HashSet<int>();
            foreach (var id in ids) distinct.Add(id);
            var sorted = distinct.ToArray();
            Array.Sort(sorted);

            var numberOf = new Dictionary<int, int>(sorted.Length);
            for (var n = 0; n < sorted.Length; n++) numberOf[sorted[n]] = n;
            for (var k = 0; k < ids.Length; k++) ids[k] = numberOf[ids[k]];
            return sorted;
        }
    }
}
