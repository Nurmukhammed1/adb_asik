using CineMatch.Importer;
using CineMatch.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;

const string Usage = """
    Loads a MovieLens dataset into the CineMatch database.

    Usage: dotnet run --project tools/CineMatch.Importer -- [options]

      --dataset <small|1m|10m|32m>   Dataset size to load (default: small)
      --reset                        Drop all CineMatch collections first (deletes registered users too)
      --tmdb <N>                     Fetch descriptions and posters from TMDB for the N most-rated movies
      --help                         Show this text

    Secrets (dotnet user-secrets set "<key>" "<value>" --project tools/CineMatch.Importer):
      Importer:DemoPassword          Password of the imported user<id> accounts (required to import)
      Importer:AdminPassword         Password of the admin account (default: the demo password)
      Tmdb:AccessToken               TMDB "API Read Access Token" (required for --tmdb)
    """;

// ---------- Arguments ----------
var datasetKey = "small";
var reset = false;
var tmdbLimit = 0;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dataset" when i + 1 < args.Length:
            datasetKey = args[++i].ToLowerInvariant();
            break;
        case "--reset":
            reset = true;
            break;
        case "--tmdb" when i + 1 < args.Length && int.TryParse(args[i + 1], out tmdbLimit) && tmdbLimit > 0:
            i++;
            break;
        case "--help" or "-h":
            Console.WriteLine(Usage);
            return 0;
        default:
            return Fail($"Unknown or incomplete option '{args[i]}'.\n\n{Usage}");
    }
}
if (!Dataset.All.TryGetValue(datasetKey, out var dataset))
    return Fail($"Unknown dataset '{datasetKey}'. Use one of: {string.Join(", ", Dataset.All.Keys)}.");

// ---------- Configuration ----------
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var mongoSettings = config.GetSection("Mongo").Get<MongoSettings>() ?? new MongoSettings();
var dataDirectory = RepositoryPaths.Resolve(config["DataDirectory"] ?? "data");
var demoPassword = config["Importer:DemoPassword"];
var adminPassword = config["Importer:AdminPassword"] ?? demoPassword;
var tmdbToken = config["Tmdb:AccessToken"];

if (tmdbLimit > 0 && string.IsNullOrWhiteSpace(tmdbToken))
    return Fail("--tmdb needs a token. Set it with:\n" +
                "  dotnet user-secrets set \"Tmdb:AccessToken\" \"<token>\" --project tools/CineMatch.Importer");

var ctx = new MongoContext(mongoSettings);
try
{
    await ctx.PingAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
}
catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
{
    return Fail("Cannot reach MongoDB. Start Docker Desktop, then run: docker compose up -d");
}

// ---------- Import ----------
var loader = new DatabaseLoader(ctx);
if (reset) await loader.ResetAsync();

if (await loader.HasDataAsync())
{
    Console.WriteLine("The database already has movies, so the import is skipped. Use --reset to reload.");
}
else
{
    if (string.IsNullOrWhiteSpace(demoPassword) || demoPassword.Length < 8)
        return Fail("Set a demo password (8+ characters) for the imported accounts first:\n" +
                    "  dotnet user-secrets set \"Importer:DemoPassword\" \"<password>\" --project tools/CineMatch.Importer");

    await dataset.EnsureDownloadedAsync(dataDirectory);
    Console.WriteLine($"Importing '{dataset.Key}' into database '{mongoSettings.Database}' ...");
    await loader.LoadAsync(new MovieLensReader(dataset, dataDirectory), demoPassword, adminPassword!);
}

if (tmdbLimit > 0) await new TmdbEnricher(ctx, tmdbToken!).RunAsync(tmdbLimit);
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
