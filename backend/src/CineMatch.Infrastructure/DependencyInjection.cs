using CineMatch.Core.Interfaces;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
