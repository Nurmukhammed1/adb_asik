using System.Globalization;
using CineMatch.Importer;
using CineMatch.Infrastructure.Mongo;
using CineMatch.Perf;
using CineMatch.Recommender;
using Microsoft.Extensions.Configuration;

const string Usage = """
    Measures how fast CineMatch is and writes the results to perf/results.

    Usage: dotnet run -c Release --project tools/CineMatch.Perf -- <command> [options]

    Commands
      prepare --dataset <small|1m|10m|32m>
            Replace the database content with one MovieLens dataset, build its similarity
            table, and record how long each part took.

      run --stage <name> [--iterations 100] [--warmup 5] [--max-seconds 60] [--api http://localhost:5196]
            Time every kind of request against the running API and capture the query plans.
            <name> labels the state being measured, for example "no-indexes" or "indexes".
            Start the API first:  dotnet run -c Release --project src/CineMatch.Api --launch-profile http

      indexes <drop|create|list>
            Drop every index except the id indexes (the baseline), create them again, or list them.

      report
            Rebuild perf/results/summary.md and the CSV files from all stored results.

    The Mongo connection and the demo password are the importer's (its appsettings.json and user secrets).
    """;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine(Usage);
    return 0;
}

// ---------- Options ----------
var command = args[0];
var options = new Dictionary<string, string>();
string? argument = null;
for (var i = 1; i < args.Length; i++)
{
    if (args[i].StartsWith("--") && i + 1 < args.Length) options[args[i][2..]] = args[++i];
    else if (argument is null && !args[i].StartsWith("--")) argument = args[i];
    else return Fail($"Unknown or incomplete option '{args[i]}'.\n\n{Usage}");
}

int Number(string name, int fallback) =>
    options.TryGetValue(name, out var text) && int.TryParse(text, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value
        : fallback;

// ---------- Configuration (shared with the importer) ----------
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(DatabaseLoader).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var mongoSettings = config.GetSection("Mongo").Get<MongoSettings>() ?? new MongoSettings();
var dataDirectory = RepositoryPaths.Resolve(config["DataDirectory"] ?? "data");
var store = new ResultStore(RepositoryPaths.Resolve(options.GetValueOrDefault("results", "perf/results")));
var demoPassword = config["Importer:DemoPassword"];

if (command == "report")
{
    store.WriteSummary();
    Console.WriteLine($"Wrote {Path.Combine(store.Directory, "summary.md")}");
    return 0;
}

var ctx = new MongoContext(mongoSettings);
try
{
    await ctx.PingAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
}
catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
{
    return Fail("Cannot reach MongoDB. Start Docker Desktop, then run: docker compose up -d");
}
var database = new Database(ctx);

switch (command)
{
    case "prepare":
    {
        if (!options.TryGetValue("dataset", out var key) || !Dataset.All.TryGetValue(key.ToLowerInvariant(), out var dataset))
            return Fail($"prepare needs --dataset with one of: {string.Join(", ", Dataset.All.Keys)}.");
        if (string.IsNullOrWhiteSpace(demoPassword))
            return Fail("The importer's user secret Importer:DemoPassword is not set.");

        var result = await database.PrepareAsync(
            dataset, dataDirectory, demoPassword, config["Importer:AdminPassword"] ?? demoPassword, new RecommenderOptions());
        store.Save(result);
        store.WriteSummary();
        Console.WriteLine($"Prepared '{dataset.Key}': {result.Data.Ratings:N0} ratings, {result.Data.DataMb:N0} MB of data, {result.Data.IndexMb:N0} MB of indexes.");
        Console.WriteLine("If the API is running, restart it or wait: it remembers the catalog's average rating for 10 minutes.");
        return 0;
    }

    case "indexes":
    {
        switch (argument)
        {
            case "drop":
                await database.DropIndexesAsync();
                Console.WriteLine("Dropped every index except the id indexes. Text search does not work until they are created again.");
                break;
            case "create":
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await database.CreateIndexesAsync();
                Console.WriteLine($"Created all indexes in {watch.Elapsed.TotalSeconds:F1}s.");
                break;
            case "list":
                break;
            default:
                return Fail("indexes needs one of: drop, create, list.");
        }
        foreach (var (collection, names) in await database.ListIndexesAsync())
            Console.WriteLine($"  {collection,-10} {(names.Count == 0 ? "(only the id index)" : string.Join(", ", names))}");
        return 0;
    }

    case "run":
    {
        if (!options.TryGetValue("stage", out var stage) || stage.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Fail("run needs --stage <name> (letters, digits, dashes), for example --stage indexes.");
        if (string.IsNullOrWhiteSpace(demoPassword))
            return Fail("The importer's user secret Importer:DemoPassword is not set.");

        var api = options.GetValueOrDefault("api", "http://localhost:5196");
        using var http = new HttpClient { BaseAddress = new Uri(api), Timeout = TimeSpan.FromMinutes(10) };
        try
        {
            (await http.GetAsync("/health")).EnsureSuccessStatusCode();
        }
        catch (HttpRequestException)
        {
            return Fail($"The API does not answer at {api}. Start it first:\n" +
                        "  dotnet run -c Release --project src/CineMatch.Api --launch-profile http");
        }

        var data = await database.DescribeDataAsync(options.GetValueOrDefault("dataset"));
        if (data.Ratings == 0) return Fail("The database is empty. Run 'prepare --dataset <size>' first.");
        var iterations = Number("iterations", 100);
        Console.WriteLine($"Stage '{stage}' on dataset '{data.Key}' ({data.Ratings:N0} ratings), {iterations} requests per scenario.");

        Console.WriteLine("Choosing test movies and users ...");
        var inputs = await TestInputs.SelectAsync(ctx);

        Console.WriteLine("Measuring response times ...");
        var runner = new LatencyRunner(http, ctx, demoPassword, iterations, Number("warmup", 5), Number("max-seconds", 60));
        var latency = await runner.RunAsync(inputs);

        Console.WriteLine("Capturing query plans ...");
        var plans = await new QueryPlans(ctx).CaptureAsync(inputs);
        foreach (var plan in plans)
            Console.WriteLine($"  {plan.Query,-58} examined {plan.DocsExamined,10:N0}  returned {plan.Returned,7:N0}  {plan.Plan}");

        store.Save(new RunResult(
            stage, data.Key, DateTime.UtcNow, data, await database.DescribeMachineAsync(), inputs.Describe(), iterations, latency, plans));
        store.WriteSummary();
        Console.WriteLine($"Saved. Summary: {Path.Combine(store.Directory, "summary.md")}");
        return 0;
    }

    default:
        return Fail($"Unknown command '{command}'.\n\n{Usage}");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
