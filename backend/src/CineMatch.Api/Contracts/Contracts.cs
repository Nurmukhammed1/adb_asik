using System.ComponentModel.DataAnnotations;
using CineMatch.Core.Models;

namespace CineMatch.Api.Contracts;

// ---------- Auth and users ----------

public class RegisterRequest
{
    [Required, RegularExpression("^[A-Za-z0-9_]{3,30}$",
        ErrorMessage = "Username must be 3-30 characters: letters, digits or underscore.")]
    public string Username { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [StringLength(60)]
    public string? DisplayName { get; set; }
}

public class LoginRequest
{
    /// <summary>Username or email.</summary>
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public class UpdateProfileRequest
{
    [Required, StringLength(60)]
    public string DisplayName { get; set; } = "";

    [MaxLength(10)]
    public List<string> FavoriteGenres { get; set; } = [];
}

public record UserDto(int Id, string Username, string Email, string Role, UserProfile Profile, DateTime CreatedAt)
{
    public static UserDto From(User user) =>
        new(user.Id, user.Username, user.Email, user.Role, user.Profile, user.CreatedAt);
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

// ---------- Catalog ----------

public class MovieRequest
{
    [Required, StringLength(300)]
    public string Title { get; set; } = "";

    [Range(1870, 2100)]
    public int? Year { get; set; }

    [Required, MinLength(1), MaxLength(10)]
    public List<string> Genres { get; set; } = [];

    [Required, StringLength(5000)]
    public string Description { get; set; } = "";

    [MaxLength(30)]
    public List<string> Tags { get; set; } = [];

    [Url, StringLength(500)]
    public string? PosterUrl { get; set; }

    public int? TmdbId { get; set; }

    public Movie ToMovie(int id = 0) => new()
    {
        Id = id,
        Title = Title.Trim(),
        Year = Year,
        Genres = Clean(Genres),
        Description = Description.Trim(),
        Tags = Clean(Tags),
        PosterUrl = PosterUrl,
        TmdbId = TmdbId,
    };

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>A movie plus, for logged-in users, their own rating and watchlist state.</summary>
public record MovieDetailsResponse(Movie Movie, double? MyRating, bool InWatchlist);

// ---------- Interactions ----------

public class RatingRequest
{
    /// <summary>0.5 to 5 in half-star steps.</summary>
    [Range(0.5, 5)]
    public double Rating { get; set; }
}

public record RatingResponse(double? Rating, RatingStats RatingStats);

/// <summary>One entry of a user's history: a rating or a view.</summary>
public record HistoryItem(string Type, MovieSummary Movie, double? Rating, DateTime Ts);

public record WatchlistEntry(MovieSummary Movie, DateTime AddedAt);
