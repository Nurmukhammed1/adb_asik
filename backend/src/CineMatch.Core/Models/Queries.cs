namespace CineMatch.Core.Models;

public enum MovieSort
{
    /// <summary>Text-match score when <c>q</c> is given, otherwise popularity.</summary>
    Relevance,
    Rating,
    Popularity,
    Year,
    Title,
}

/// <summary>Filters, sorting and paging for the movie list.</summary>
public class MovieQuery
{
    public const int MaxPageSize = 100;

    /// <summary>Text search over title and tags.</summary>
    public string? Q { get; set; }

    public string? Genre { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }

    /// <summary>Only movies with at least this many ratings.</summary>
    public int? MinRatings { get; set; }

    public MovieSort Sort { get; set; } = MovieSort.Relevance;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Page number and size, forced into the allowed range.</summary>
public readonly record struct Paging(int Page, int PageSize)
{
    public int Skip => (Page - 1) * PageSize;

    public static Paging Normalize(int page, int pageSize) =>
        new(Math.Max(page, 1), Math.Clamp(pageSize, 1, MovieQuery.MaxPageSize));
}
