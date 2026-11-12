using System.Runtime.InteropServices;
using CineMatch.Core.Models;

namespace CineMatch.Recommender;

/// <summary>A movie the user has not rated, with the scores computed for it.</summary>
/// <param name="Evidence">Sum over the user's rated movies of similarity * (rating - user's average).</param>
/// <param name="PredictedRating">The rating the user is expected to give, 0.5 to 5.</param>
/// <param name="BecauseOf">The rated movie that contributed most, or null if nothing spoke for it.</param>
public readonly record struct Candidate(int MovieId, double Evidence, double PredictedRating, int? BecauseOf);

/// <summary>
/// Item-based collaborative filtering, online part. Pure functions over a user's ratings and the stored
/// neighbor lists, so the live API and the offline accuracy check run exactly the same code.
/// </summary>
public static class ItemBasedScorer
{
    public const double MinRating = 0.5;
    public const double MaxRating = 5;

    /// <summary>
    /// Every movie the user rated passes its opinion on to its similar movies: a movie rated above the
    /// user's average pushes its neighbors up, one rated below pushes them down.
    /// </summary>
    public static List<Candidate> Recommend(
        IReadOnlyCollection<Rating> userRatings,
        Func<int, IReadOnlyList<Neighbor>?> neighborsOf,
        RankBy rankBy,
        double damping,
        int limit)
    {
        if (userRatings.Count == 0 || limit <= 0) return [];

        var mean = userRatings.Average(r => r.Value);
        var rated = userRatings.Select(r => r.MovieId).ToHashSet();
        var sums = new Dictionary<int, Sums>();

        foreach (var rating in userRatings)
        {
            var neighbors = neighborsOf(rating.MovieId);
            if (neighbors is null) continue;

            var opinion = rating.Value - mean;
            foreach (var neighbor in neighbors)
            {
                if (rated.Contains(neighbor.MovieId)) continue;

                ref var s = ref CollectionsMarshal.GetValueRefOrAddDefault(sums, neighbor.MovieId, out var seen);
                var contribution = neighbor.Score * opinion;
                s.Evidence += contribution;
                s.Weight += neighbor.Score;
                if (!seen || contribution > s.BestContribution)
                {
                    s.BestContribution = contribution;
                    s.BestSource = rating.MovieId;
                }
            }
        }

        var candidates = sums.Select(pair => (
            Candidate: new Candidate(
                pair.Key,
                pair.Value.Evidence,
                Clamp(mean + pair.Value.Evidence / (pair.Value.Weight + damping)),
                pair.Value.BestContribution > 0 ? pair.Value.BestSource : null),
            pair.Value.Weight));

        // Ties (for example a user who gave every movie the same rating) fall back to total similarity.
        var ranked = rankBy == RankBy.Evidence
            ? candidates.Where(c => c.Candidate.Evidence >= 0)
                .OrderByDescending(c => c.Candidate.Evidence).ThenByDescending(c => c.Weight)
            : candidates.OrderByDescending(c => c.Candidate.PredictedRating).ThenByDescending(c => c.Weight);

        return ranked.ThenBy(c => c.Candidate.MovieId).Take(limit).Select(c => c.Candidate).ToList();
    }

    /// <summary>
    /// Predicts one user's rating of one movie from the movie's neighbors that the user has rated:
    /// user's average + sum(similarity * (rating - average)) / (sum(similarity) + damping).
    /// Returns null if the user rated none of the neighbors.
    /// </summary>
    public static double? PredictRating(
        IReadOnlyDictionary<int, double> userRatings, double userMean, IReadOnlyList<Neighbor>? neighborsOfTarget, double damping)
    {
        if (neighborsOfTarget is null) return null;

        double evidence = 0, weight = 0;
        foreach (var neighbor in neighborsOfTarget)
        {
            if (!userRatings.TryGetValue(neighbor.MovieId, out var rating)) continue;
            evidence += neighbor.Score * (rating - userMean);
            weight += neighbor.Score;
        }
        return weight > 0 ? Clamp(userMean + evidence / (weight + damping)) : null;
    }

    private static double Clamp(double rating) => Math.Clamp(rating, MinRating, MaxRating);

    private struct Sums
    {
        public double Evidence;
        public double Weight;
        public double BestContribution;
        public int BestSource;
    }
}
