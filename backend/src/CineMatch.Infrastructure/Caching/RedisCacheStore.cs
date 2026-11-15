using System.Text.Json;
using CineMatch.Core.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CineMatch.Infrastructure.Caching;

/// <summary>
/// <see cref="ICacheStore"/> on Redis: each key is a Redis hash, each field a JSON value.
/// Any Redis failure is logged and treated as a cache miss, so the API keeps working without Redis.
/// </summary>
public sealed class RedisCacheStore : ICacheStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private readonly ConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheStore> _logger;
    private long _lastWarningTicks;

    public RedisCacheStore(string connectionString, ILogger<RedisCacheStore> logger)
    {
        _logger = logger;
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false; // start even if Redis is down, and reconnect when it comes back
        options.ConnectTimeout = 2_000;
        options.SyncTimeout = 1_000;
        options.AsyncTimeout = 1_000;
        _redis = ConnectionMultiplexer.Connect(options);
    }

    public async Task<T?> GetAsync<T>(string key, string field) where T : class
    {
        if (!Connected()) return null;
        try
        {
            var value = await _redis.GetDatabase().HashGetAsync(key, field);
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value.ToString(), Json);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Warn(ex.Message);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, string field, T value, TimeSpan lifetime) where T : class
    {
        if (!Connected()) return;
        try
        {
            var db = _redis.GetDatabase();
            await db.HashSetAsync(key, field, JsonSerializer.Serialize(value, Json));
            await db.KeyExpireAsync(key, lifetime);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Warn(ex.Message);
        }
    }

    public async Task RemoveAsync(string key)
    {
        if (!Connected()) return;
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            Warn(ex.Message);
        }
    }

    public void Dispose() => _redis.Dispose();

    // While Redis is down, skip it at once instead of waiting for a timeout on every request.
    // The client keeps reconnecting in the background, and the cache is used again when it is back.
    private bool Connected()
    {
        if (_redis.IsConnected) return true;
        Warn("not connected");
        return false;
    }

    private static bool IsCacheFailure(Exception ex) =>
        ex is RedisException or TimeoutException or JsonException or ObjectDisposedException;

    // Without Redis every request would fail the same way, so say it at most once a minute.
    private void Warn(string reason)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last < WarningInterval.Ticks || Interlocked.CompareExchange(ref _lastWarningTicks, now, last) != last) return;
        _logger.LogWarning("Redis cache unavailable, answering without it: {Reason}", reason);
    }
}

/// <summary>The cache when no Redis is configured: never holds anything.</summary>
public sealed class NoCacheStore : ICacheStore
{
    public Task<T?> GetAsync<T>(string key, string field) where T : class => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, string field, T value, TimeSpan lifetime) where T : class => Task.CompletedTask;

    public Task RemoveAsync(string key) => Task.CompletedTask;
}
