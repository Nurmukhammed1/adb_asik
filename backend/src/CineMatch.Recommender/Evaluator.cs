using System.Diagnostics;
using CineMatch.Core.Models;

namespace CineMatch.Recommender;

/// <summary>Quality of a top-N list, averaged over the evaluated users.</summary>
/// <param name="Precision">Share of the recommended movies that the user really liked.</param>
/// <param name="Recall">Share of the movies the user really liked that were recommended.</param>
/// <param name="CatalogCoverage">Share of all movies that showed up in at least one list.</param>
public record TopNQuality(double Precision, double Recall, double CatalogCoverage);

public record EvaluationReport(
    int Users,
    int Movies,
    int TrainRatings,
    int TestRatings,
    double TestFraction,
    double SimilarityBuildSeconds,

    // Rating prediction: lower is better.
    double RmseItemBased,
    double RmseUserAverage,
    double RmseGlobalAverage,
    double PredictionCoverage,

    // Top-N lists: higher is better.
    int ListSize,
    double LikedThreshold,
    int TopNUsers,
    TopNQuality ItemBasedByEvidence,
    TopNQuality ItemBasedByPredictedRating,
    TopNQuality MostPopular);

/// <summary>
/// Offline accuracy check. Hides part of every user's ratings, builds the similarity table from the
/// rest, and measures how well the hidden ratings are predicted and how many hidden "liked" movies
/// the recommendation lists find. Two simple baselines show whether the algorithm earns its keep.
/// </summary>
public static class Evaluator
{
    /// <summary>Users with fewer ratings than this are kept whole in the training part.</summary>
    private const int MinRatingsToSplit = 10;

    public static EvaluationReport Run(
        IEnumerable<Rating> allRatings,
        RecommenderOptions options,
        double testFraction = 0.2,
        int listSize = 10,
        double likedThreshold = 4.0,
        int maxTopNUsers = 2000,
        int seed = 42,
        CancellationToken ct = default)
    {
        // ---------- 1. Split every user's ratings into a training and a hidden test part ----------
        var train = new List<Rating>();
        var testByUser = new Dictionary<int, List<Rating>>();
        foreach (var user in allRatings.GroupBy(r => r.UserId))
        {
            var ratings = user.OrderBy(r => r.MovieId).ToArray();
            if (ratings.Length < MinRatingsToSplit)
            {
                train.AddRange(ratings);
                continue;
            }

            new Random(HashCode.Combine(seed, user.Key)).Shuffle(ratings);   // repeatable for a given seed
            var hidden = (int)Math.Round(ratings.Length * testFraction);
            testByUser[user.Key] = ratings[..hidden].ToList();
            train.AddRange(ratings[hidden..]);
        }

        // ---------- 2. Build the similarity table from the training part only ----------
        var matrix = RatingMatrix.From(train);
        var watch = Stopwatch.StartNew();
        var built = SimilarityBuilder.Build(matrix, options, ct);
        var buildSeconds = watch.Elapsed.TotalSeconds;

        var neighborsOf = new Dictionary<int, Neighbor[]>(matrix.ItemCount);
        for (var item = 0; item < matrix.ItemCount; item++) neighborsOf[matrix.MovieIds[item]] = built[item];
        var userNumber = new Dictionary<int, int>(matrix.UserCount);
        for (var u = 0; u < matrix.UserCount; u++) userNumber[matrix.UserIds[u]] = u;

        var globalMean = train.Average(r => r.Value);
        var mostRated = train.GroupBy(r => r.MovieId)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => g.Key)
            .ToArray();

        // ---------- 3. Rating prediction on the hidden ratings ----------
        double errorItemBased = 0, errorUserMean = 0, errorGlobalMean = 0;
        int testCount = 0, predicted = 0;
        foreach (var (userId, hidden) in testByUser)
        {
            ct.ThrowIfCancellationRequested();
            var known = matrix.RatingsOfUser(userNumber[userId]).ToDictionary(r => r.MovieId, r => r.Value);
            var userMean = known.Values.Average();

            foreach (var rating in hidden)
            {
                var prediction = ItemBasedScorer.PredictRating(
                    known, userMean, neighborsOf.GetValueOrDefault(rating.MovieId), options.PredictionDamping);
                if (prediction is not null) predicted++;

                // Without any rated neighbor the best available guess is the user's own average.
                errorItemBased += Square((prediction ?? userMean) - rating.Value);
                errorUserMean += Square(userMean - rating.Value);
                errorGlobalMean += Square(globalMean - rating.Value);
                testCount++;
            }
        }

        // ---------- 4. Top-N lists: do they contain the hidden movies the user liked? ----------
        var topNUsers = testByUser
            .Where(pair => pair.Value.Any(r => r.Value >= likedThreshold))
            .Select(pair => pair.Key)
            .Order()
            .ToArray();
        new Random(seed).Shuffle(topNUsers);
        topNUsers = topNUsers.Take(maxTopNUsers).ToArray();

        var byEvidence = new TopNCounter(matrix.ItemCount);
        var byPrediction = new TopNCounter(matrix.ItemCount);
        var byPopularity = new TopNCounter(matrix.ItemCount);
        foreach (var userId in topNUsers)
        {
            ct.ThrowIfCancellationRequested();
            var known = matrix.RatingsOfUser(userNumber[userId]);
            var seen = known.Select(r => r.MovieId).ToHashSet();
            var liked = testByUser[userId].Where(r => r.Value >= likedThreshold).Select(r => r.MovieId).ToHashSet();

            byEvidence.Add(Recommend(RankBy.Evidence), liked, listSize);
            byPrediction.Add(Recommend(RankBy.PredictedRating), liked, listSize);
            byPopularity.Add(mostRated.Where(id => !seen.Contains(id)).Take(listSize), liked, listSize);

            IEnumerable<int> Recommend(RankBy rankBy) => ItemBasedScorer
                .Recommend(known, id => neighborsOf.GetValueOrDefault(id), rankBy, options.PredictionDamping, listSize)
                .Select(c => c.MovieId);
        }

        return new EvaluationReport(
            matrix.UserCount,
            matrix.ItemCount,
            train.Count,
            testCount,
            testFraction,
            Math.Round(buildSeconds, 2),
            Rmse(errorItemBased, testCount),
            Rmse(errorUserMean, testCount),
            Rmse(errorGlobalMean, testCount),
            testCount > 0 ? Math.Round((double)predicted / testCount, 4) : 0,
            listSize,
            likedThreshold,
            topNUsers.Length,
            byEvidence.Result(),
            byPrediction.Result(),
            byPopularity.Result());
    }

    private static double Square(double x) => x * x;

    private static double Rmse(double squaredErrors, int count) =>
        count > 0 ? Math.Round(Math.Sqrt(squaredErrors / count), 4) : 0;

    private sealed class TopNCounter(int catalogSize)
    {
        private readonly HashSet<int> _recommended = [];
        private double _precision, _recall;
        private int _users;

        public void Add(IEnumerable<int> list, HashSet<int> liked, int listSize)
        {
            var hits = 0;
            foreach (var movieId in list)
            {
                _recommended.Add(movieId);
                if (liked.Contains(movieId)) hits++;
            }
            _precision += (double)hits / listSize;
            _recall += (double)hits / liked.Count;
            _users++;
        }

        public TopNQuality Result() => _users == 0
            ? new TopNQuality(0, 0, 0)
            : new TopNQuality(
                Math.Round(_precision / _users, 4),
                Math.Round(_recall / _users, 4),
                Math.Round((double)_recommended.Count / catalogSize, 4));
    }
}
