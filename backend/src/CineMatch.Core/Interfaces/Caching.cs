namespace CineMatch.Core.Interfaces;

/// <summary>
/// A cache of finished answers, grouped under keys: one key holds several values, each under its own field
/// (for example one user's recommendation lists of different lengths). Removing a key drops all of its fields.
/// </summary>
/// <remarks>
/// The cache is an optimization only. If it cannot be reached, reads return null and writes do nothing,
/// so callers fall back to computing the answer.
/// </remarks>
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, string field) where T : class;

    /// <summary>Stores a value; <paramref name="lifetime"/> applies to the whole key.</summary>
    Task SetAsync<T>(string key, string field, T value, TimeSpan lifetime) where T : class;

    Task RemoveAsync(string key);
}
