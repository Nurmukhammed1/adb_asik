using CineMatch.Api.Auth;
using CineMatch.Api.Contracts;
using CineMatch.Core.Interfaces;
using CineMatch.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CineMatch.Api.Controllers;

public enum HistoryType
{
    Ratings,
    Views,
}

[ApiController]
[Authorize]
[Route("api/users/me")]
public class UsersController(
    IUserRepository users,
    IMovieRepository movies,
    IRatingRepository ratings,
    IEventRepository events,
    IWatchlistRepository watchlist) : ControllerBase
{
    private int UserId => User.GetUserId();

    /// <summary>The logged-in user's account and profile.</summary>
    [HttpGet]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken ct)
    {
        var user = await users.GetByIdAsync(UserId, ct);
        return user is null ? NotFound() : UserDto.From(user);
    }

    /// <summary>Changes the display name and favorite genres.</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request, CancellationToken ct)
    {
        var profile = new UserProfile
        {
            DisplayName = request.DisplayName.Trim(),
            FavoriteGenres = request.FavoriteGenres
                .Select(g => g.Trim())
                .Where(g => g.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        if (!await users.UpdateProfileAsync(UserId, profile, ct)) return NotFound();

        var user = await users.GetByIdAsync(UserId, ct);
        return user is null ? NotFound() : UserDto.From(user);
    }

    /// <summary>The user's ratings or views, newest first.</summary>
    [HttpGet("history")]
    public async Task<PagedResult<HistoryItem>> GetHistory(
        HistoryType type = HistoryType.Ratings, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var paging = Paging.Normalize(page, pageSize);

        if (type == HistoryType.Views)
        {
            var views = await events.GetByUserAsync(UserId, EventTypes.View, paging, ct);
            var viewed = await SummariesByIdAsync(views.Items.Select(v => v.MovieId), ct);
            var viewItems = views.Items
                .Where(v => viewed.ContainsKey(v.MovieId))
                .Select(v => new HistoryItem("view", viewed[v.MovieId], null, v.Ts))
                .ToList();
            return new PagedResult<HistoryItem>(viewItems, views.Total, views.Page, views.PageSize);
        }

        var rated = await ratings.GetByUserAsync(UserId, paging, ct);
        var ratedMovies = await SummariesByIdAsync(rated.Items.Select(r => r.MovieId), ct);
        var ratingItems = rated.Items
            .Where(r => ratedMovies.ContainsKey(r.MovieId))
            .Select(r => new HistoryItem("rating", ratedMovies[r.MovieId], r.Value, r.Ts))
            .ToList();
        return new PagedResult<HistoryItem>(ratingItems, rated.Total, rated.Page, rated.PageSize);
    }

    /// <summary>The user's watchlist, newest first.</summary>
    [HttpGet("watchlist")]
    public async Task<PagedResult<WatchlistEntry>> GetWatchlist(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var saved = await watchlist.GetByUserAsync(UserId, Paging.Normalize(page, pageSize), ct);
        var savedMovies = await SummariesByIdAsync(saved.Items.Select(w => w.MovieId), ct);
        var entries = saved.Items
            .Where(w => savedMovies.ContainsKey(w.MovieId))
            .Select(w => new WatchlistEntry(savedMovies[w.MovieId], w.AddedAt))
            .ToList();
        return new PagedResult<WatchlistEntry>(entries, saved.Total, saved.Page, saved.PageSize);
    }

    /// <summary>Adds a movie to the watchlist. Adding it twice is harmless.</summary>
    [HttpPost("watchlist/{movieId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddToWatchlist(int movieId, CancellationToken ct)
    {
        if (!await movies.ExistsAsync(movieId, ct)) return NotFound();
        await watchlist.AddAsync(UserId, movieId, ct);
        return NoContent();
    }

    /// <summary>Removes a movie from the watchlist.</summary>
    [HttpDelete("watchlist/{movieId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFromWatchlist(int movieId, CancellationToken ct) =>
        await watchlist.RemoveAsync(UserId, movieId, ct) ? NoContent() : NotFound();

    private async Task<Dictionary<int, MovieSummary>> SummariesByIdAsync(IEnumerable<int> movieIds, CancellationToken ct)
    {
        var summaries = await movies.GetSummariesAsync(movieIds.Distinct().ToList(), ct);
        return summaries.ToDictionary(m => m.Id);
    }
}
