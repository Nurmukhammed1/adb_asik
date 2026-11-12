using CineMatch.Core.Models;

namespace CineMatch.Recommender;

/// <summary>
/// Item-based collaborative filtering, offline part: for every movie, finds the movies that the same
/// users rated in the same way.
///
/// Similarity is the adjusted cosine over the users who rated both movies i and j:
///
///     sim(i, j) = sum(a_ui * a_uj) / (sqrt(sum(a_ui^2)) * sqrt(sum(a_uj^2)))  *  n / (n + shrinkage)
///
/// where a_ui is user u's rating of movie i minus that user's average, and n is the number of
/// common raters. The last factor scores down pairs that only a few users rated in common.
/// </summary>
public static class SimilarityBuilder
{
    /// <summary>
    /// Returns, for each internal movie number, its most similar movies (best first, movie ids).
    /// Movies run in parallel; each worker has its own scratch arrays.
    /// </summary>
    public static Neighbor[][] Build(RatingMatrix matrix, RecommenderOptions options, CancellationToken ct = default)
    {
        var result = new Neighbor[matrix.ItemCount][];
        Parallel.For(
            0,
            matrix.ItemCount,
            new ParallelOptions { CancellationToken = ct },
            () => new Workspace(matrix.ItemCount),
            (item, _, workspace) =>
            {
                result[item] = NeighborsOf(item, matrix, options, workspace);
                return workspace;
            },
            _ => { });
        return result;
    }

    private static Neighbor[] NeighborsOf(int item, RatingMatrix m, RecommenderOptions options, Workspace w)
    {
        // Walk over everyone who rated this movie, and over everything else each of them rated.
        // Every such (user, other movie) step adds one term to the sums of the pair (item, other).
        for (var p = m.ItemStart[item]; p < m.ItemStart[item + 1]; p++)
        {
            var user = m.ItemUsers[p];
            var mine = m.ItemValues[p];
            for (var q = m.UserStart[user]; q < m.UserStart[user + 1]; q++)
            {
                var other = m.UserItems[q];
                if (other == item) continue;

                var theirs = m.UserValues[q];
                if (w.Common[other]++ == 0) w.Touched[w.TouchedCount++] = other;
                w.Dot[other] += mine * theirs;
                w.NormMine[other] += mine * mine;
                w.NormTheirs[other] += theirs * theirs;
            }
        }

        // Keep the best few in a small heap whose root is the weakest kept candidate.
        var best = w.Best;
        for (var t = 0; t < w.TouchedCount; t++)
        {
            var other = w.Touched[t];
            var common = w.Common[other];
            var norm = Math.Sqrt((double)w.NormMine[other] * w.NormTheirs[other]);
            if (common >= options.MinCommonRaters && norm > 0)
            {
                var similarity = w.Dot[other] / norm * (common / (common + options.Shrinkage));
                if (similarity > 0)
                {
                    if (best.Count < options.NeighborsPerMovie)
                        best.Enqueue(other, similarity);
                    else if (best.TryPeek(out _, out var weakest) && similarity > weakest)
                        best.EnqueueDequeue(other, similarity);
                }
            }

            // Reset only what was used, so the scratch arrays are clean for the next movie.
            w.Common[other] = 0;
            w.Dot[other] = 0;
            w.NormMine[other] = 0;
            w.NormTheirs[other] = 0;
        }
        w.TouchedCount = 0;

        var neighbors = new Neighbor[best.Count];
        for (var n = neighbors.Length - 1; n >= 0; n--)   // the heap gives the weakest first
        {
            best.TryDequeue(out var other, out var similarity);
            neighbors[n] = new Neighbor { MovieId = m.MovieIds[other], Score = Math.Round(similarity, 4) };
        }
        return neighbors;
    }

    /// <summary>Scratch space of one worker thread: running sums for every possible "other" movie.</summary>
    private sealed class Workspace(int itemCount)
    {
        public readonly int[] Common = new int[itemCount];
        public readonly float[] Dot = new float[itemCount];
        public readonly float[] NormMine = new float[itemCount];
        public readonly float[] NormTheirs = new float[itemCount];
        public readonly int[] Touched = new int[itemCount];
        public int TouchedCount;
        public readonly PriorityQueue<int, double> Best = new();
    }
}
