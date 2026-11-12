namespace CineMatch.Core.Models;

/// <summary>A catalog item. Stored in the <c>movies</c> collection.</summary>
public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }

    /// <summary>The item's categories.</summary>
    public List<string> Genres { get; set; } = [];

    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? PosterUrl { get; set; }
    public int? TmdbId { get; set; }

    /// <summary>Kept up to date on every rating, so lists can sort by rating without aggregating.</summary>
    public RatingStats RatingStats { get; set; } = new();
}

public class RatingStats
{
    public int Count { get; set; }
    public double Sum { get; set; }
    public double Avg { get; set; }
}

/// <summary>The fields of a movie that list views need (no description or tags).</summary>
public class MovieSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public List<string> Genres { get; set; } = [];
    public string? PosterUrl { get; set; }
    public RatingStats RatingStats { get; set; } = new();
}
