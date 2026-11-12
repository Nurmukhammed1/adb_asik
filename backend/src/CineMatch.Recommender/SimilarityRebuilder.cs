using System.Diagnostics;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;

namespace CineMatch.Recommender;

/// <summary>What one rebuild of the similarity table read, computed and stored, and how long each part took.</summary>
public record RebuildResult(
    DateTime StartedAt,
    int Users,
    int Movies,
    int Ratings,
    long MoviesWithNeighbors,
    long StoredPairs,
    double LoadSeconds,
    double BuildSeconds,
    double SaveSeconds);

/// <summary>Reads all ratings, computes every movie's neighbors and swaps the result into the database.</summary>
public sealed class SimilarityRebuilder(IRatingRepository ratings, ISimilarityRepository similarities, RecommenderOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning => _gate.CurrentCount == 0;
    public RebuildResult? LastResult { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Runs a rebuild. Returns null, without doing anything, if one is already running.</summary>
    public async Task<RebuildResult?> RunAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return null;
        try
        {
            var startedAt = DateTime.UtcNow;
            var watch = Stopwatch.StartNew();

            var expected = (int)Math.Min(await ratings.EstimateCountAsync(ct), int.MaxValue / 2);
            var builder = new RatingMatrix.Builder(expected);
            await foreach (var rating in ratings.StreamAllAsync(ct)) builder.Add(rating.UserId, rating.MovieId, rating.Value);
            var matrix = builder.Build();
            var loadSeconds = watch.Elapsed.TotalSeconds;

            // CPU-bound and parallel: keep it off the request thread.
            watch.Restart();
            var neighbors = await Task.Run(() => SimilarityBuilder.Build(matrix, options, ct), ct);
            var buildSeconds = watch.Elapsed.TotalSeconds;

            watch.Restart();
            var computedAt = DateTime.UtcNow;
            var entries = Enumerable.Range(0, matrix.ItemCount)
                .Where(item => neighbors[item].Length > 0)
                .Select(item => new ItemSimilarity { Id = matrix.MovieIds[item], Neighbors = neighbors[item], ComputedAt = computedAt });
            var stored = await similarities.ReplaceAllAsync(entries, ct);

            LastResult = new RebuildResult(
                startedAt,
                matrix.UserCount,
                matrix.ItemCount,
                matrix.RatingCount,
                stored,
                neighbors.Sum(n => (long)n.Length),
                Math.Round(loadSeconds, 2),
                Math.Round(buildSeconds, 2),
                Math.Round(watch.Elapsed.TotalSeconds, 2));
            LastError = null;
            return LastResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
