using CineMatch.Api.Auth;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Recommender;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CineMatch.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(RecommendationService recommender) : ControllerBase
{
    /// <summary>Personal recommendations for the logged-in user.</summary>
    /// <remarks>
    /// Item-based collaborative filtering over the user's ratings. Users with fewer than five ratings
    /// get popular movies from their favorite genres instead; each item's "source" says which it is.
    /// </remarks>
    [HttpGet("me")]
    [Authorize]
    public Task<RecommendationList> ForMe(int limit = 20, CancellationToken ct = default) =>
        recommender.ForUserAsync(User.GetUserId(), limit, ct);
}

/// <summary>State of the similarity table that the recommendations are computed from.</summary>
public record RecommenderStatus(
    bool Running, long MoviesWithNeighbors, RebuildResult? LastRebuild, string? LastError, RecommenderOptions Settings);

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/recommender")]
public class RecommenderAdminController(
    SimilarityRebuilder rebuilder,
    ISimilarityRepository similarities,
    IRatingRepository ratings,
    RecommenderOptions options,
    ILogger<RecommenderAdminController> logger) : ControllerBase
{
    /// <summary>The in-API accuracy check keeps every rating in memory, so it is limited to small datasets.</summary>
    private const long MaxRatingsToEvaluate = 2_000_000;

    /// <summary>Whether a rebuild is running, what the last one did, and the current settings.</summary>
    [HttpGet("status")]
    public async Task<RecommenderStatus> GetStatus(CancellationToken ct) =>
        new(rebuilder.IsRunning, await similarities.CountAsync(ct), rebuilder.LastResult, rebuilder.LastError, options);

    /// <summary>Recomputes the similarity table from all ratings.</summary>
    /// <remarks>
    /// By default the rebuild runs in the background and the call returns 202 at once.
    /// With wait=true the call returns the result when the rebuild has finished.
    /// </remarks>
    [HttpPost("rebuild")]
    [ProducesResponseType<RebuildResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<RecommenderStatus>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rebuild(bool wait = false, CancellationToken ct = default)
    {
        if (wait)
        {
            var result = await rebuilder.RunAsync(ct);
            return result is null ? AlreadyRunning() : Ok(result);
        }

        if (rebuilder.IsRunning) return AlreadyRunning();
        _ = Task.Run(async () =>
        {
            try
            {
                await rebuilder.RunAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background rebuild of the similarity table failed.");
            }
        }, CancellationToken.None);
        return Accepted(new RecommenderStatus(true, await similarities.CountAsync(ct), rebuilder.LastResult, null, options));
    }

    /// <summary>Measures recommendation accuracy by hiding part of every user's ratings.</summary>
    /// <remarks>
    /// Reports the rating-prediction error (RMSE) and the quality of top-N lists (precision, recall),
    /// next to simple baselines. Works on a copy in memory; the stored similarity table is not touched.
    /// </remarks>
    [HttpPost("evaluate")]
    [ProducesResponseType<EvaluationReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Evaluate(
        double testFraction = 0.2, int listSize = 10, int maxUsers = 2000, CancellationToken ct = default)
    {
        if (testFraction is <= 0 or >= 1 || listSize is < 1 or > 100 || maxUsers < 1)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "testFraction must be between 0 and 1, listSize from 1 to 100, maxUsers at least 1.");
        if (await ratings.EstimateCountAsync(ct) > MaxRatingsToEvaluate)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"The in-API accuracy check is limited to {MaxRatingsToEvaluate:N0} ratings.");

        var all = new List<Rating>();
        await foreach (var rating in ratings.StreamAllAsync(ct)) all.Add(rating);
        if (all.Count == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "There are no ratings to evaluate.");

        var report = await Task.Run(
            () => Evaluator.Run(all, options, testFraction, listSize, maxTopNUsers: maxUsers, ct: ct), ct);
        return Ok(report);
    }

    private ObjectResult AlreadyRunning() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "A rebuild is already running.");
}
