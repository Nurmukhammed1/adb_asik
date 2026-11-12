using CineMatch.Core.Models;

namespace CineMatch.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Finds a user by username or email (case-insensitive).</summary>
    Task<User?> GetByLoginAsync(string usernameOrEmail, CancellationToken ct = default);

    /// <summary>Assigns the next id and inserts. Throws <see cref="ConflictException"/> on a duplicate username or email.</summary>
    Task<User> CreateAsync(User user, CancellationToken ct = default);

    Task<bool> UpdateProfileAsync(int id, UserProfile profile, CancellationToken ct = default);
}

public interface IMovieRepository
{
    Task<PagedResult<MovieSummary>> SearchAsync(MovieQuery query, CancellationToken ct = default);
    Task<Movie?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);

    /// <summary>Returns the summaries in the same order as <paramref name="ids"/>; missing movies are skipped.</summary>
    Task<IReadOnlyList<MovieSummary>> GetSummariesAsync(IReadOnlyList<int> ids, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetGenresAsync(CancellationToken ct = default);

    /// <summary>Assigns the next id and inserts.</summary>
    Task<Movie> CreateAsync(Movie movie, CancellationToken ct = default);

    /// <summary>Replaces the editable fields; rating statistics are left untouched.</summary>
    Task<Movie?> UpdateAsync(Movie movie, CancellationToken ct = default);

    /// <summary>Deletes the movie together with its ratings, watchlist entries and similarity entry.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>The average of all ratings in the catalog (3.5 while there are none).</summary>
    Task<double> GetGlobalMeanRatingAsync(CancellationToken ct = default);

    /// <summary>
    /// The best-rated movies, where a movie with few ratings is pulled towards <paramref name="priorMean"/>:
    /// score = (sum + priorWeight * priorMean) / (count + priorWeight). Unrated movies are left out.
    /// </summary>
    Task<IReadOnlyList<ScoredMovie>> GetPopularAsync(
        IReadOnlyCollection<string>? genres, IReadOnlyCollection<int> excludeIds, int limit,
        double priorMean, int priorWeight, CancellationToken ct = default);
}

public interface IRatingRepository
{
    Task<Rating?> GetAsync(int userId, int movieId, CancellationToken ct = default);

    /// <summary>
    /// Creates or changes a rating and updates the movie's statistics in one transaction.
    /// Returns the new statistics, or null if the movie does not exist.
    /// </summary>
    Task<RatingStats?> SetAsync(int userId, int movieId, double value, CancellationToken ct = default);

    /// <summary>
    /// Removes a rating and updates the movie's statistics in one transaction.
    /// Returns the new statistics, or null if there was no rating.
    /// </summary>
    Task<RatingStats?> RemoveAsync(int userId, int movieId, CancellationToken ct = default);

    /// <summary>A user's ratings, newest first.</summary>
    Task<PagedResult<Rating>> GetByUserAsync(int userId, Paging paging, CancellationToken ct = default);

    /// <summary>All ratings of one user (user, movie and value only).</summary>
    Task<IReadOnlyList<Rating>> GetAllByUserAsync(int userId, CancellationToken ct = default);

    /// <summary>Every rating in the database as a stream (user, movie and value only).</summary>
    IAsyncEnumerable<Rating> StreamAllAsync(CancellationToken ct = default);

    /// <summary>A fast, approximate count of all ratings.</summary>
    Task<long> EstimateCountAsync(CancellationToken ct = default);
}

public interface ISimilarityRepository
{
    Task<ItemSimilarity?> GetAsync(int movieId, CancellationToken ct = default);

    /// <summary>The similarity entries of several movies in one query.</summary>
    Task<IReadOnlyList<ItemSimilarity>> GetManyAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct = default);

    /// <summary>Swaps in a completely new set of entries; readers never see a half-written set.</summary>
    Task<long> ReplaceAllAsync(IEnumerable<ItemSimilarity> entries, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);
}

public interface IEventRepository
{
    Task AddAsync(int userId, int movieId, string type, CancellationToken ct = default);

    /// <summary>A user's events of one type, newest first.</summary>
    Task<PagedResult<InteractionEvent>> GetByUserAsync(int userId, string type, Paging paging, CancellationToken ct = default);
}

public interface IWatchlistRepository
{
    /// <summary>Adds the movie if it is not already on the list.</summary>
    Task AddAsync(int userId, int movieId, CancellationToken ct = default);

    Task<bool> RemoveAsync(int userId, int movieId, CancellationToken ct = default);
    Task<bool> ContainsAsync(int userId, int movieId, CancellationToken ct = default);

    /// <summary>A user's watchlist, newest first.</summary>
    Task<PagedResult<WatchlistItem>> GetByUserAsync(int userId, Paging paging, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
