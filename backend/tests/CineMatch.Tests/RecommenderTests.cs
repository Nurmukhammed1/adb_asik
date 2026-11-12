using CineMatch.Core.Models;
using CineMatch.Recommender;

namespace CineMatch.Tests;

/// <summary>The recommender's algorithms on small hand-made data. These tests need no database.</summary>
public class RecommenderTests
{
    private const int A = 1, B = 2, C = 3, D = 4;   // movies

    private static readonly RecommenderOptions Options = new() { MinCommonRaters = 5, Shrinkage = 20, NeighborsPerMovie = 50 };

    /// <summary>
    /// Six users. A and B are always rated alike, C always the opposite way, D is rated by only two users.
    /// </summary>
    private static List<Rating> SixUsers()
    {
        var ratings = new List<Rating>();
        for (var user = 1; user <= 6; user++)
        {
            var fan = user % 2 == 1;
            ratings.Add(Rate(user, A, fan ? 5 : 1));
            ratings.Add(Rate(user, B, fan ? 5 : 1));
            ratings.Add(Rate(user, C, fan ? 1 : 5));
            if (user <= 2) ratings.Add(Rate(user, D, 5));
        }
        return ratings;
    }

    // ---------- Rating matrix ----------

    [Fact]
    public void Matrix_groups_ratings_both_ways_and_centers_them_on_each_users_average()
    {
        var matrix = RatingMatrix.From([Rate(10, 7, 5), Rate(10, 9, 3), Rate(20, 7, 2)]);

        Assert.Equal([10, 20], matrix.UserIds);
        Assert.Equal([7, 9], matrix.MovieIds);
        Assert.Equal([4f, 2f], matrix.UserMeans);
        Assert.Equal(3, matrix.RatingCount);

        // Movie 7 (number 0) was rated by both users: +1 from user 10 (5 - 4) and 0 from user 20 (2 - 2).
        var ratersOfFirstMovie = Enumerable.Range(matrix.ItemStart[0], matrix.ItemStart[1] - matrix.ItemStart[0])
            .Select(p => (matrix.UserIds[matrix.ItemUsers[p]], matrix.ItemValues[p]));
        Assert.Equal([(10, 1f), (20, 0f)], ratersOfFirstMovie);

        var restored = matrix.RatingsOfUser(0);
        Assert.Equal([(7, 5.0), (9, 3.0)], restored.Select(r => (r.MovieId, r.Value)));
    }

    // ---------- Similarity ----------

    [Fact]
    public void Movies_rated_alike_become_neighbors_and_opposites_do_not()
    {
        var matrix = RatingMatrix.From(SixUsers());

        var neighbors = Neighbors(matrix, Options);

        // A and B agree for all 6 common raters: cosine 1, scored down by 6 / (6 + 20).
        var ofA = Assert.Single(neighbors[A]);
        Assert.Equal(B, ofA.MovieId);
        Assert.Equal(Math.Round(6 / 26.0, 4), ofA.Score);
        Assert.Equal(A, Assert.Single(neighbors[B]).MovieId);

        Assert.Empty(neighbors[C]);   // only negative similarities, which are never stored
    }

    [Fact]
    public void Pairs_with_too_few_common_raters_are_ignored()
    {
        var matrix = RatingMatrix.From(SixUsers());

        var strict = Neighbors(matrix, Options);
        var lenient = Neighbors(matrix, new RecommenderOptions { MinCommonRaters = 1, Shrinkage = 20 });

        Assert.Empty(strict[D]);
        Assert.DoesNotContain(strict[A], n => n.MovieId == D);
        Assert.NotEmpty(lenient[D]);   // the same pair counts once the threshold is lowered
    }

    [Fact]
    public void More_common_raters_mean_a_higher_score_for_the_same_agreement()
    {
        // B is rated by all 10 fans of A, C by only 5 of them; both always agree with A.
        var ratings = new List<Rating>();
        for (var user = 1; user <= 10; user++)
        {
            var value = user % 2 == 1 ? 5 : 2;
            ratings.Add(Rate(user, A, value));
            ratings.Add(Rate(user, B, value));
            if (user <= 5) ratings.Add(Rate(user, C, value));
            ratings.Add(Rate(user, D, 6 - value));   // gives every user a spread, so centered values are not zero
        }

        var ofA = Neighbors(RatingMatrix.From(ratings), Options)[A];

        Assert.Equal([B, C], ofA.Select(n => n.MovieId));
        Assert.True(ofA[0].Score > ofA[1].Score);
    }

    [Fact]
    public void Only_the_best_neighbors_are_kept_best_first()
    {
        var ratings = new List<Rating>();
        var random = new Random(1);
        for (var user = 1; user <= 40; user++)
        {
            var taste = random.Next(1, 6);
            for (var movie = 1; movie <= 12; movie++)
                ratings.Add(Rate(user, movie, Math.Clamp(taste + random.Next(-2, 3) * (movie % 3), 1, 5)));
        }

        var neighbors = Neighbors(RatingMatrix.From(ratings), new RecommenderOptions { NeighborsPerMovie = 3, Shrinkage = 20 });

        Assert.All(neighbors.Values, list =>
        {
            Assert.True(list.Length <= 3);
            Assert.Equal(list.OrderByDescending(n => n.Score).Select(n => n.Score), list.Select(n => n.Score));
            Assert.All(list, n => Assert.InRange(n.Score, 0.0001, 1));
        });
        Assert.Contains(neighbors.Values, list => list.Length == 3);
    }

    // ---------- Scoring ----------

    private static readonly Dictionary<int, Neighbor[]> Stored = new()
    {
        // The user rated 100 (liked) and 200 (disliked); 301-303 are unseen.
        [100] = [Near(301, 0.8), Near(302, 0.4)],
        [200] = [Near(302, 0.9), Near(303, 0.5), Near(100, 0.3)],
    };

    private static readonly Rating[] LikedAndDisliked = [Rate(1, 100, 5), Rate(1, 200, 1)];   // average 3

    [Fact]
    public void Liked_movies_push_their_neighbors_up_and_disliked_ones_push_theirs_down()
    {
        var list = ItemBasedScorer.Recommend(LikedAndDisliked, id => Stored.GetValueOrDefault(id), RankBy.Evidence, damping: 1, limit: 10);

        // 301: 0.8 * (5 - 3) = +1.6.   302: 0.4 * 2 + 0.9 * (1 - 3) = -1.0.   303: 0.5 * -2 = -1.0.
        var only = Assert.Single(list);
        Assert.Equal(301, only.MovieId);
        Assert.Equal(1.6, only.Evidence, 10);
        Assert.Equal(3 + 1.6 / (0.8 + 1), only.PredictedRating, 10);
        Assert.Equal(100, only.BecauseOf);
    }

    [Fact]
    public void Ranking_by_predicted_rating_keeps_every_candidate_and_never_returns_a_rated_movie()
    {
        var list = ItemBasedScorer.Recommend(LikedAndDisliked, id => Stored.GetValueOrDefault(id), RankBy.PredictedRating, damping: 1, limit: 10);

        Assert.Equal([301, 302, 303], list.Select(c => c.MovieId));   // 100 is a neighbor of 200 but already rated
        Assert.Equal(3 - 1.0 / (1.3 + 1), list[1].PredictedRating, 10);
        Assert.Null(list[2].BecauseOf);   // nothing speaks for 303
    }

    [Fact]
    public void Predicted_ratings_stay_on_the_rating_scale_and_the_limit_is_respected()
    {
        Rating[] generous = [Rate(1, 100, 5), Rate(1, 200, 5), Rate(1, 300, 4.5)];
        var stored = new Dictionary<int, Neighbor[]> { [100] = [Near(301, 1), Near(302, 0.9), Near(303, 0.8)] };

        var list = ItemBasedScorer.Recommend(generous, id => stored.GetValueOrDefault(id), RankBy.Evidence, damping: 0, limit: 2);

        Assert.Equal(2, list.Count);
        Assert.All(list, c => Assert.InRange(c.PredictedRating, ItemBasedScorer.MinRating, ItemBasedScorer.MaxRating));
    }

    [Fact]
    public void A_user_without_ratings_or_without_stored_neighbors_gets_an_empty_list()
    {
        Assert.Empty(ItemBasedScorer.Recommend([], id => Stored.GetValueOrDefault(id), RankBy.Evidence, 1, 10));
        Assert.Empty(ItemBasedScorer.Recommend(LikedAndDisliked, _ => null, RankBy.Evidence, 1, 10));
    }

    [Fact]
    public void A_single_rating_is_predicted_from_the_neighbors_the_user_rated()
    {
        var known = new Dictionary<int, double> { [100] = 5, [200] = 1 };
        Neighbor[] ofTarget = [Near(100, 0.6), Near(200, 0.2), Near(999, 0.9)];   // 999 is not rated: ignored

        var prediction = ItemBasedScorer.PredictRating(known, userMean: 3, ofTarget, damping: 1);
        var noOverlap = ItemBasedScorer.PredictRating(known, 3, [Near(999, 0.9)], 1);

        Assert.Equal(3 + (0.6 * 2 + 0.2 * -2) / (0.8 + 1), prediction!.Value, 10);
        Assert.Null(noOverlap);
        Assert.Null(ItemBasedScorer.PredictRating(known, 3, null, 1));
    }

    // ---------- Accuracy check ----------

    [Fact]
    public void On_data_with_two_clear_taste_groups_the_recommender_beats_both_baselines()
    {
        // Group 0 loves movies 1-30 and dislikes 31-60; group 1 is the other way round.
        // A handful of blockbusters (61-66) are rated by nearly everyone but only found average.
        var random = new Random(7);
        var ratings = new List<Rating>();
        for (var user = 1; user <= 120; user++)
        {
            var group = user % 2;
            for (var movie = 1; movie <= 60; movie++)
            {
                if (random.NextDouble() > 0.8) continue;
                var loved = (movie <= 30) == (group == 0);
                ratings.Add(Rate(user, movie, loved ? random.Next(4, 6) : random.Next(1, 3)));
            }
            for (var movie = 61; movie <= 66; movie++)
            {
                if (random.NextDouble() < 0.9) ratings.Add(Rate(user, movie, 3));
            }
        }

        var report = Evaluator.Run(ratings, new RecommenderOptions());

        Assert.Equal(120, report.Users);
        Assert.Equal(ratings.Count, report.TrainRatings + report.TestRatings);
        Assert.InRange((double)report.TestRatings / ratings.Count, 0.18, 0.22);

        // Both groups rate half the movies high and half low, so every user's average is about the same
        // as the global one: the two average-based baselines are equally bad here, and only the
        // item-based prediction can tell the halves apart.
        Assert.True(report.RmseItemBased < report.RmseUserAverage, $"{report.RmseItemBased} vs {report.RmseUserAverage}");
        Assert.True(report.RmseItemBased < report.RmseGlobalAverage, $"{report.RmseItemBased} vs {report.RmseGlobalAverage}");

        // A user rates about 24 of their group's 30 movies and about 5 of those are hidden, which leaves
        // about 11 unseen movies of the right group. A perfect 10-item list therefore finds nearly all
        // hidden liked movies (recall near 0.9) and about 4.4 of its 10 items are hits (precision near 0.44).
        var itemBased = report.ItemBasedByEvidence;
        var numbers = $"item-based {itemBased}, most popular {report.MostPopular}";
        Assert.True(itemBased.Recall > 0.8, numbers);
        Assert.True(itemBased.Precision > 0.35, numbers);
        Assert.True(itemBased.Precision > 1.5 * report.MostPopular.Precision, numbers);
    }

    [Fact]
    public void The_same_seed_gives_the_same_report()
    {
        var ratings = SixUsers();
        for (var user = 1; user <= 6; user++)
            for (var movie = 10; movie < 25; movie++) ratings.Add(Rate(user, movie, 1 + (user * movie) % 5));

        var first = Evaluator.Run(ratings, Options, seed: 3);
        var second = Evaluator.Run(ratings, Options, seed: 3);

        Assert.Equal(first with { SimilarityBuildSeconds = 0 }, second with { SimilarityBuildSeconds = 0 });
        Assert.True(first.TestRatings > 0);
    }

    private static Rating Rate(int user, int movie, double value) => new() { UserId = user, MovieId = movie, Value = value };

    private static Neighbor Near(int movie, double score) => new() { MovieId = movie, Score = score };

    private static Dictionary<int, Neighbor[]> Neighbors(RatingMatrix matrix, RecommenderOptions options)
    {
        var built = SimilarityBuilder.Build(matrix, options);
        return Enumerable.Range(0, matrix.ItemCount).ToDictionary(item => matrix.MovieIds[item], item => built[item]);
    }
}
