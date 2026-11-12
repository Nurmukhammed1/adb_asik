using CineMatch.Core.Interfaces;
using CineMatch.Recommender;

namespace CineMatch.Api.Infrastructure;

/// <summary>
/// Keeps the similarity table fresh without anyone asking: builds it at startup if it is missing,
/// then rebuilds it every night, when new ratings have had a day to come in.
/// </summary>
public class RecommenderScheduler(
    SimilarityRebuilder rebuilder,
    ISimilarityRepository similarities,
    IRatingRepository ratings,
    RecommenderOptions options,
    ILogger<RecommenderScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (options.BuildOnStartupIfEmpty
                && await similarities.CountAsync(stoppingToken) == 0
                && await ratings.EstimateCountAsync(stoppingToken) > 0)
            {
                await RebuildAsync("startup", stoppingToken);
            }

            if (options.NightlyRebuildHour is not { } hour) return;
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var next = now.Date.AddHours(Math.Clamp(hour, 0, 23));
                if (next <= now) next = next.AddDays(1);

                await Task.Delay(next - now, stoppingToken);
                await RebuildAsync("nightly", stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The API is shutting down.
        }
    }

    private async Task RebuildAsync(string reason, CancellationToken ct)
    {
        try
        {
            var result = await rebuilder.RunAsync(ct);
            if (result is null)
            {
                logger.LogInformation("Skipped the {Reason} rebuild: another rebuild is running.", reason);
                return;
            }
            logger.LogInformation(
                "Similarity table rebuilt ({Reason}): {Ratings:N0} ratings, {Movies:N0} movies with neighbors, " +
                "load {Load}s, build {Build}s, save {Save}s.",
                reason, result.Ratings, result.MoviesWithNeighbors, result.LoadSeconds, result.BuildSeconds, result.SaveSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The {Reason} rebuild of the similarity table failed.", reason);
        }
    }
}
