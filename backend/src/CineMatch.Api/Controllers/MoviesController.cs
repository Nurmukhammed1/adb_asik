using CineMatch.Api.Auth;
using CineMatch.Api.Contracts;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using CineMatch.Recommender;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CineMatch.Api.Controllers;

[ApiController]
[Route("api/movies")]
public class MoviesController(
    IMovieRepository movies,
    IRatingRepository ratings,
    IEventRepository events,
    IWatchlistRepository watchlist,
    RecommendationService recommender) : ControllerBase
{
    // ---------- Catalog (public) ----------

    /// <summary>Searches and browses the catalog with filters, sorting and paging.</summary>
    [HttpGet]
    public Task<PagedResult<MovieSummary>> Search([FromQuery] MovieQuery query, CancellationToken ct) =>
        movies.SearchAsync(query, ct);

    /// <summary>One movie. With a login token, also the caller's own rating and watchlist state.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<MovieDetailsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MovieDetailsResponse>> GetById(int id, CancellationToken ct)
    {
        var movie = await movies.GetByIdAsync(id, ct);
        if (movie is null) return NotFound();

        if (User.GetUserIdOrNull() is not { } userId)
            return new MovieDetailsResponse(movie, null, false);

        var myRating = await ratings.GetAsync(userId, id, ct);
        var inWatchlist = await watchlist.ContainsAsync(userId, id, ct);
        return new MovieDetailsResponse(movie, myRating?.Value, inWatchlist);
    }

    /// <summary>All genres used in the catalog.</summary>
    [HttpGet("/api/genres")]
    public Task<IReadOnlyList<string>> GetGenres(CancellationToken ct) => movies.GetGenresAsync(ct);

    // ---------- Recommendations that need no login ----------

    /// <summary>Movies that the same users rated in the same way ("people who liked this also liked").</summary>
    /// <remarks>Read from the precomputed similarity table. A movie with too few ratings gets popular movies of its genres.</remarks>
    [HttpGet("{id:int}/similar")]
    [ProducesResponseType<IReadOnlyList<Recommendation>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSimilar(int id, int limit = 20, CancellationToken ct = default)
    {
        var similar = await recommender.SimilarToAsync(id, limit, ct);
        return similar is null ? NotFound() : Ok(similar);
    }

    /// <summary>The best-rated movies, overall or in one genre.</summary>
    /// <remarks>Ranked by an average that is pulled towards the catalog average while a movie has few ratings.</remarks>
    [HttpGet("popular")]
    public Task<IReadOnlyList<Recommendation>> GetPopular(string? genre = null, int limit = 20, CancellationToken ct = default) =>
        recommender.PopularAsync(genre, limit, ct);

    // ---------- Catalog management (admin) ----------

    /// <summary>Adds a movie to the catalog.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<Movie>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(MovieRequest request, CancellationToken ct)
    {
        var movie = await movies.CreateAsync(request.ToMovie(), ct);
        return CreatedAtAction(nameof(GetById), new { id = movie.Id }, movie);
    }

    /// <summary>Replaces a movie's editable fields. Rating statistics are kept.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Movie>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Movie>> Update(int id, MovieRequest request, CancellationToken ct)
    {
        var movie = await movies.UpdateAsync(request.ToMovie(id), ct);
        return movie is null ? NotFound() : movie;
    }

    /// <summary>Deletes a movie together with its ratings and watchlist entries.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await movies.DeleteAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Interactions (logged-in users) ----------

    /// <summary>Rates a movie, or changes an existing rating.</summary>
    [HttpPut("{id:int}/rating")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RatingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RatingResponse>> Rate(int id, RatingRequest request, CancellationToken ct)
    {
        var halfSteps = request.Rating * 2;
        if (Math.Abs(halfSteps - Math.Round(halfSteps)) > 1e-9)
        {
            ModelState.AddModelError(nameof(request.Rating), "Rating must be in half-star steps: 0.5, 1, 1.5 ... 5.");
            return ValidationProblem(ModelState);
        }

        var stats = await ratings.SetAsync(User.GetUserId(), id, request.Rating, ct);
        return stats is null ? NotFound() : new RatingResponse(request.Rating, stats);
    }

    /// <summary>Removes the caller's rating of a movie.</summary>
    [HttpDelete("{id:int}/rating")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RatingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RatingResponse>> Unrate(int id, CancellationToken ct)
    {
        var stats = await ratings.RemoveAsync(User.GetUserId(), id, ct);
        return stats is null ? NotFound() : new RatingResponse(null, stats);
    }

    /// <summary>Records that the caller viewed a movie.</summary>
    [HttpPost("{id:int}/views")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordView(int id, CancellationToken ct)
    {
        if (!await movies.ExistsAsync(id, ct)) return NotFound();
        await events.AddAsync(User.GetUserId(), id, EventTypes.View, ct);
        return NoContent();
    }
}
