using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CineMatch.Api.Contracts;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Infrastructure.Mongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace CineMatch.Tests;

/// <summary>
/// Starts a throwaway MongoDB (one-node replica set, like docker-compose) and runs the real API
/// in-process against it. Shared by all test classes, so tests create their own uniquely named data.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "Test-password-1";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").WithReplicaSet("rs0").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();

        // Connect straight to the one node; its replica-set host name only resolves inside the container.
        var url = new MongoUrlBuilder(_mongo.GetConnectionString()) { ReplicaSetName = null, DirectConnection = true };

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = url.ToString(),
                ["Mongo:Database"] = "cinematch_tests",
                ["Jwt:SigningKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ["Recommender:BuildOnStartupIfEmpty"] = "false", // tests rebuild exactly when they mean to
            }));
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    public MongoContext Mongo => _factory.Services.GetRequiredService<MongoContext>();

    /// <summary>A client with no login token.</summary>
    public HttpClient Anonymous() => _factory.CreateClient();

    /// <summary>Registers a new user through the API and returns a client that sends their token.</summary>
    public async Task<(HttpClient Client, UserDto User)> NewUserAsync()
    {
        var name = Unique("user");
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Username = name, Email = $"{name}@example.com", Password = Password });
        response.EnsureSuccessStatusCode();

        var auth = await response.ReadAsync<AuthResponse>();
        return (client.WithToken(auth.Token), auth.User);
    }

    /// <summary>Creates an admin directly in the database (there is no API for that) and logs in.</summary>
    public async Task<HttpClient> NewAdminAsync()
    {
        var name = Unique("admin");
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await _factory.Services.GetRequiredService<IUserRepository>().CreateAsync(new User
        {
            Username = name,
            Email = $"{name}@example.com",
            PasswordHash = hasher.Hash(Password),
            Role = Roles.Admin,
            CreatedAt = DateTime.UtcNow,
        });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = name, Password = Password });
        response.EnsureSuccessStatusCode();
        return client.WithToken((await response.ReadAsync<AuthResponse>()).Token);
    }

    /// <summary>Adds a movie directly through the repository.</summary>
    public Task<Movie> NewMovieAsync(string? title = null, string[]? genres = null, int? year = 2000, string[]? tags = null) =>
        _factory.Services.GetRequiredService<IMovieRepository>().CreateAsync(new Movie
        {
            Title = title ?? Unique("Movie"),
            Year = year,
            Genres = [.. genres ?? ["Drama"]],
            Description = "A test movie.",
            Tags = [.. tags ?? []],
        });

    /// <summary>A lowercase, letters-only unique word, so it is also a single token for text search.</summary>
    public static string Unique(string prefix) =>
        prefix.ToLowerInvariant() + string.Concat(Guid.NewGuid().ToByteArray().Take(8).Select(b => (char)('a' + b % 26)));
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<T> GetAsync<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<T>();
    }

    /// <summary>The field names listed under "errors" in a 400 validation response.</summary>
    public static async Task<string[]> ReadErrorFieldsAsync(this HttpResponseMessage response)
    {
        var problem = await response.ReadAsync<JsonElement>();
        return problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).ToArray();
    }

    public static Task<HttpResponseMessage> RateAsync(this HttpClient client, int movieId, double rating) =>
        client.PutAsJsonAsync($"/api/movies/{movieId}/rating", new RatingRequest { Rating = rating });
}
