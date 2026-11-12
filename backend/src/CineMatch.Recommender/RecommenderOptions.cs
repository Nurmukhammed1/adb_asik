namespace CineMatch.Recommender;

/// <summary>How the final recommendation list is ordered.</summary>
public enum RankBy
{
    /// <summary>
    /// By summed evidence: every movie the user liked votes for its neighbors, weighted by similarity.
    /// Movies supported by many liked movies come first.
    /// </summary>
    Evidence,

    /// <summary>By the predicted rating (a weighted average). Can put obscure movies with one strong neighbor on top.</summary>
    PredictedRating,
}

/// <summary>Settings of the recommender. Bound from the <c>Recommender</c> configuration section.</summary>
public class RecommenderOptions
{
    /// <summary>How many similar movies are stored per movie.</summary>
    public int NeighborsPerMovie { get; set; } = 50;

    /// <summary>Two movies are only compared if at least this many users rated both.</summary>
    public int MinCommonRaters { get; set; } = 5;

    /// <summary>
    /// Scores down pairs with few common raters: similarity is multiplied by n / (n + Shrinkage),
    /// where n is the number of users who rated both movies.
    /// </summary>
    public double Shrinkage { get; set; } = 50;

    /// <summary>Users with fewer ratings than this get popular movies instead of personal ones.</summary>
    public int MinRatingsForPersonal { get; set; } = 5;

    /// <summary>Pulls a predicted rating towards the user's own average when little evidence supports it.</summary>
    public double PredictionDamping { get; set; } = 1;

    public RankBy RankBy { get; set; } = RankBy.Evidence;

    /// <summary>
    /// For "popular" lists: a movie's average is mixed with this many imaginary ratings at the catalog
    /// average, so a movie with three 5-star ratings does not beat one with a thousand 4.5-star ratings.
    /// </summary>
    public int PopularPriorWeight { get; set; } = 50;

    /// <summary>Local hour (0-23) of the nightly rebuild of the similarity table; null switches it off.</summary>
    public int? NightlyRebuildHour { get; set; } = 3;

    /// <summary>Build the similarity table when the API starts and finds ratings but no table.</summary>
    public bool BuildOnStartupIfEmpty { get; set; } = true;
}
