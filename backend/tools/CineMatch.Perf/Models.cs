using CineMatch.Importer;
using CineMatch.Recommender;

namespace CineMatch.Perf;

/// <summary>The computer the numbers were measured on; they only compare fairly on the same machine.</summary>
public record MachineInfo(string Os, string Runtime, int Cores, double MemoryGb, string MongoVersion);

/// <summary>What is in the database at the time of a measurement.</summary>
public record DatasetInfo(
    string Key, long Movies, long Users, long Ratings, long MoviesWithNeighbors, double DataMb, double IndexMb);

/// <summary>Timings of loading one dataset size and building its similarity table.</summary>
public record PrepareResult(
    string Dataset, DateTime PreparedAt, ImportResult Import, RebuildResult Similarity, DatasetInfo Data, MachineInfo Machine);

/// <summary>Response times of one kind of request, in milliseconds, measured at the client.</summary>
public record LatencyResult(
    string Scenario,
    string Description,
    int Requests,
    int Errors,
    double MinMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double MeanMs,
    string? Note)
{
    /// <summary>Summarizes the response times of the successful requests.</summary>
    public static LatencyResult From(string scenario, string description, IReadOnlyList<double> milliseconds, int errors, string? note)
    {
        if (milliseconds.Count == 0)
            return new LatencyResult(scenario, description, errors, errors, 0, 0, 0, 0, 0, 0, note);

        var sorted = milliseconds.Order().ToArray();
        return new LatencyResult(
            scenario,
            description,
            sorted.Length + errors,
            errors,
            Round(sorted[0]),
            Round(Percentile(sorted, 0.50)),
            Round(Percentile(sorted, 0.95)),
            Round(Percentile(sorted, 0.99)),
            Round(sorted[^1]),
            Round(sorted.Average()),
            note);
    }

    /// <summary>Nearest-rank percentile: the smallest value that at least the given share of samples do not exceed.</summary>
    public static double Percentile(double[] sorted, double share)
    {
        var rank = (int)Math.Ceiling(share * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private static double Round(double ms) => Math.Round(ms, 2);
}

/// <summary>How MongoDB executed one of the API's main queries.</summary>
/// <param name="DocsExamined">Documents read from disk or cache. Far above <paramref name="Returned"/> means wasted work.</param>
/// <param name="KeysExamined">Index entries read.</param>
/// <param name="Plan">The execution stages from the outside in, for example "LIMIT > FETCH > IXSCAN(index name)".</param>
public record QueryPlan(
    string Query, string Collection, long DocsExamined, long KeysExamined, long Returned, long ServerMillis, string Plan);

/// <summary>The imported accounts the measurements log in as, described by how many ratings they have.</summary>
public record TestUsers(int TypicalUsers, int TypicalRatings, int HeavyUsers, int HeaviestRatings);

/// <summary>One measurement run: one optimization stage on one dataset size.</summary>
public record RunResult(
    string Stage,
    string Dataset,
    DateTime MeasuredAt,
    DatasetInfo Data,
    MachineInfo Machine,
    TestUsers Users,
    int Iterations,
    IReadOnlyList<LatencyResult> Latency,
    IReadOnlyList<QueryPlan> Plans);
