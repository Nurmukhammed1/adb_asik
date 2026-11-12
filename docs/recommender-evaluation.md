# Recommender accuracy and settings

Measured on MovieLens small (610 users, 9,724 rated movies, 100,836 ratings) on 2026-11-10.
The numbers can be reproduced with `POST /api/admin/recommender/evaluate` (admin token).

## Method

1. For every user with at least 10 ratings, 20% of the ratings are hidden at random (fixed seed).
2. The similarity table is built from the remaining 80% only.
3. **Rating prediction:** each hidden rating is predicted from the neighbors of that movie the user
   has rated. The error is reported as RMSE (lower is better). Where the user rated none of the
   neighbors, the user's own average is used; "coverage" is the share of hidden ratings that did get
   a neighbor-based prediction.
4. **Top-10 lists:** each user gets 10 recommendations. A hidden movie counts as "liked" if it was
   rated 4.0 or higher. Precision is the share of the 10 recommendations that are liked hidden
   movies; recall is the share of the user's liked hidden movies that were recommended. Catalog
   coverage is the share of all movies that appear in at least one user's list.

Because only the hidden 20% can count as a hit, precision values look low in absolute terms; what
matters is the comparison with the baselines measured the same way.

## Result with the default settings

| Rating prediction | RMSE |
|---|---|
| **Item-based collaborative filtering** | **0.872** |
| Baseline: the user's own average | 0.936 |
| Baseline: the global average | 1.042 |

Neighbor-based predictions were possible for 82% of the hidden ratings.

| Top-10 lists (602 users) | Precision | Recall | Catalog coverage |
|---|---|---|---|
| **Item-based, ranked by evidence (default)** | **0.136** | **0.103** | **4.2%** |
| Item-based, ranked by predicted rating | 0.085 | 0.061 | 6.2% |
| Baseline: most-rated movies | 0.118 | 0.103 | 0.6% |

- The item-based predictions are 7% more accurate than the user-average baseline.
- Ranked by evidence, the lists are more precise than "most popular" and draw on seven times as
  many different movies, so users get personal lists and not the same blockbusters.
- Ranking by predicted rating is clearly worse for lists: it puts obscure movies with one strong
  neighbor on top. This is why `RankBy` is `Evidence` and the predicted rating is only shown as the
  score.

## How the settings were chosen

Each row is the average of three different train/test splits. One setting is varied at a time; the
others stay at the default (neighbors 50, minimum common raters 5, shrinkage 50, damping 1).
"Most popular" scores precision 0.119 and recall 0.103 on the same splits.

**Shrinkage** (similarity is multiplied by n / (n + shrinkage), n = users who rated both movies)

| Shrinkage | RMSE | Precision@10 | Recall@10 | Catalog coverage |
|---|---|---|---|---|
| 0 | 0.911 | 0.017 | 0.010 | 11.4% |
| 5 | 0.882 | 0.074 | 0.048 | 7.9% |
| 10 | 0.877 | 0.104 | 0.072 | 6.2% |
| 20 | 0.874 | 0.125 | 0.089 | 4.9% |
| **50** | **0.875** | **0.139** | **0.105** | **3.9%** |
| 100 | 0.879 | 0.146 | 0.115 | 3.6% |

Without shrinkage, pairs that five users happened to rate alike get a perfect similarity and
crowd out the well-supported pairs: lists become almost useless. 50 keeps the prediction error at
its best level and beats the popularity baseline on precision; 100 gains a little precision but
loses prediction accuracy and variety.

**Neighbors stored per movie** (shrinkage 20)

| Neighbors | RMSE | Precision@10 | Recall@10 |
|---|---|---|---|
| 20 | 0.879 | 0.131 | 0.097 |
| **50** | **0.874** | **0.125** | **0.089** |
| 100 | 0.873 | 0.120 | 0.085 |
| 200 | 0.873 | 0.117 | 0.083 |

Beyond 50 the prediction error no longer improves, while storage and scoring cost keep growing.

**Minimum common raters** (shrinkage 20)

| Minimum | RMSE | Prediction coverage | Precision@10 |
|---|---|---|---|
| 2 | 0.868 | 91% | 0.124 |
| **5** | **0.874** | **82%** | **0.125** |
| 10 | 0.884 | 70% | 0.126 |
| 20 | 0.898 | 51% | 0.129 |

A higher minimum removes neighbors for less-rated movies (coverage falls quickly) for almost no
gain in precision. 5 keeps obviously accidental pairs out and still covers most ratings.

**Prediction damping** (shrinkage 20): RMSE is 0.904 at 0, 0.875 at 0.5, **0.874 at 1**, 0.877 at 2
and 0.888 at 5. It does not affect the order of the lists.

## Limits of these numbers

- MovieLens small is a small, dense dataset. The best shrinkage depends on how many users rate each
  pair of movies, so it should be measured again on the larger datasets.
- Offline precision only rewards movies the user had already found and rated. It cannot reward a
  good recommendation of a movie the user has never seen, so it understates real usefulness.
