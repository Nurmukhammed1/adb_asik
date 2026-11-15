using CineMatch.Core.Interfaces;
using CineMatch.Infrastructure.Caching;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CineMatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoSettings>().Bind(configuration.GetSection("Mongo"));
        services.AddSingleton(sp => new MongoContext(sp.GetRequiredService<IOptions<MongoSettings>>().Value));
        services.AddSingleton<IndexInitializer>();

        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IMovieRepository, MovieRepository>();
        services.AddSingleton<IRatingRepository, RatingRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();
        services.AddSingleton<IWatchlistRepository, WatchlistRepository>();
        services.AddSingleton<ISimilarityRepository, SimilarityRepository>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        // Redis caches finished recommendation lists. Without a connection string the API simply computes every answer.
        // The setting is read when the cache is first needed, so configuration added later (tests) is seen.
        services.AddSingleton<ICacheStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConfiguration>()["Redis:ConnectionString"];
            return string.IsNullOrWhiteSpace(redis)
                ? new NoCacheStore()
                : new RedisCacheStore(redis, sp.GetRequiredService<ILogger<RedisCacheStore>>());
        });
        return services;
    }
}

public class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
