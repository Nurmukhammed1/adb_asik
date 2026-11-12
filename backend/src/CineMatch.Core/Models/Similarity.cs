namespace CineMatch.Core.Models;

/// <summary>
/// The movies most similar to one movie, computed offline from the ratings.
/// Stored in the <c>item_similarities</c> collection; the id is the movie id.
/// </summary>
public class ItemSimilarity
{
    public int Id { get; set; }

    /// <summary>Most similar first.</summary>
    public Neighbor[] Neighbors { get; set; } = [];

    public DateTime ComputedAt { get; set; }
}

public class Neighbor
{
    public int MovieId { get; set; }

    /// <summary>Similarity, above 0 and at most 1.</summary>
    public double Score { get; set; }
}

/// <summary>A movie with a ranking score.</summary>
public record ScoredMovie(MovieSummary Movie, double Score);
