# CineMatch

A movie streaming platform with personal recommendations, built on a NoSQL database.
Course project for Assignment #6 (data layer and backend) and the Final Project (recommendation
system, user interface, performance testing).

- **Users** browse and search a movie catalog, rate movies, keep a watchlist, and get recommendations.
- **Recommendations** come from item-based collaborative filtering: movies that the same people rated
  the same way are "similar", and a user is recommended the movies similar to the ones they liked.
- **Data** is the public [MovieLens](https://grouplens.org/datasets/movielens/) dataset.

| Part | Technology |
|---|---|
| Database | MongoDB 8 (one-node replica set), Redis (reserved for caching, see [Status](#status)) |
| Backend | ASP.NET Core Web API, .NET 10, C# |
| Frontend | Angular 22, TypeScript, Angular Material |
| Tests | xUnit + Testcontainers (backend), Vitest (frontend) |

## Contents

- [What you need](#what-you-need)
- [Quick start: everything in Docker](#quick-start-everything-in-docker)
- [Running from source](#running-from-source)
- [Loading the data](#loading-the-data)
- [Signing in](#signing-in)
- [Tests](#tests)
- [Project layout](#project-layout)
- [How it works](#how-it-works)
- [API](#api)
- [Performance measurements](#performance-measurements)
- [Status](#status)
- [Troubleshooting](#troubleshooting)
- [Data sources](#data-sources)

## What you need

| Tool | Version | Needed for |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | any recent | MongoDB and Redis; optionally the API and the site |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10 | loading the data; running the API from source; backend tests |
| [Node.js](https://nodejs.org/) | 22.22, 24.15 or 26+ | running the site from source; frontend tests |

All commands below are run from the repository root unless a step says otherwise. Docker Desktop must be
running before any `docker` command.

## Quick start: everything in Docker

**1. Create the settings file.** Copy `.env.example` to `.env` and set `JWT_SIGNING_KEY` to any random
text of 32 or more characters. It signs the login tokens. `.env` is not committed.

**2. Build and start** the database, the API and the site:

```bash
docker compose --profile app up -d --build
```

The first build downloads the .NET, Node and nginx base images and takes a few minutes.

**3. Load the data** (see [Loading the data](#loading-the-data); this step uses the .NET SDK):

```bash
dotnet user-secrets set "Importer:DemoPassword" "choose-a-password" --project backend/tools/CineMatch.Importer
```

```bash
dotnet run --project backend/tools/CineMatch.Importer -- --dataset small
```

**4. Restart the API** so it builds the table of similar movies from the new ratings:

```bash
docker compose restart api
```

**5. Open the site:**

| Address | What |
|---|---|
| http://localhost:8080 | The site |
| http://localhost:5196/scalar/v1 | API documentation, with a "try it" console |

To stop everything: `docker compose --profile app down`. The data stays in Docker volumes; add `-v` to
delete it too.

## Running from source

Use this while developing: the API and the site reload when their code changes.

**1. Start the databases:**

```bash
docker compose up -d
```

**2. Set the two secrets** (once per machine). They are stored outside the repository by
[.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).

```bash
dotnet user-secrets set "Jwt:SigningKey" "any-random-text-of-32-or-more-characters" --project backend/src/CineMatch.Api
```

```bash
dotnet user-secrets set "Importer:DemoPassword" "choose-a-password" --project backend/tools/CineMatch.Importer
```

**3. Load the data:**

```bash
dotnet run --project backend/tools/CineMatch.Importer -- --dataset small
```

**4. Start the API** (terminal 1). On its first start with data it builds the table of similar movies.

```bash
dotnet run --project backend/src/CineMatch.Api --launch-profile http
```

**5. Start the site** (terminal 2):

```bash
npm --prefix frontend install
```

```bash
npm --prefix frontend start
```

| Address | What |
|---|---|
| http://localhost:4200 | The site (development server) |
| http://localhost:5196/scalar/v1 | API documentation |

The development server passes every `/api` request on to the API, so no CORS setup is needed.

> The API from source and the API container both use port 5196. Stop the container first if it is
> running: `docker compose --profile app stop api web`.

## Loading the data

The importer downloads a MovieLens dataset into `data/` (not committed), loads movies and ratings into
MongoDB, creates one account per MovieLens user plus an admin, and builds the indexes.

```bash
dotnet run --project backend/tools/CineMatch.Importer -- --dataset <size> [--reset] [--tmdb N]
```

| Option | Meaning |
|---|---|
| `--dataset small` | 100,000 ratings, 9,700 movies, 610 users (default; loads in seconds) |
| `--dataset 1m` | 1 million ratings, 3,900 movies, 6,000 users (about 20 seconds) |
| `--dataset 10m` | 10 million ratings |
| `--dataset 32m` | 32 million ratings |
| `--reset` | Delete everything first, including accounts created on the site. Needed to switch datasets |
| `--tmdb N` | Fetch real descriptions and posters for the N most-rated movies (see below) |

If the database already has movies, the importer does nothing unless `--reset` is given.

**After loading, the table of similar movies must be rebuilt**: restart the API, or sign in as `admin`
and use *Recommender → Rebuild now*. It is also rebuilt every night at 03:00.

**Posters and descriptions (optional).** MovieLens has titles, genres and tags, but no descriptions or
posters, so movies get a stand-in description and a colored card. To fetch the real ones, create a free
[TMDB](https://www.themoviedb.org/settings/api) account, copy the **API Read Access Token** (the long
one), and run:

```bash
dotnet user-secrets set "Tmdb:AccessToken" "your-token" --project backend/tools/CineMatch.Importer
```

```bash
dotnet run --project backend/tools/CineMatch.Importer -- --tmdb 500
```

This works for the `small` and `32m` datasets, the two that include TMDB ids.

## Signing in

| Account | Username | Password |
|---|---|---|
| Your own | create one on the site (*Sign up*) | your choice |
| Imported MovieLens users | `user1`, `user2`, `user3`, ... | the `Importer:DemoPassword` you set |
| Administrator | `admin` | the same, unless `Importer:AdminPassword` is set |

To see the passwords stored on this machine:

```bash
dotnet user-secrets list --project backend/tools/CineMatch.Importer
```

An imported user such as `user1` already has ratings, so *For You* shows personal recommendations at
once. A new account gets popular movies until it has rated five.

## Tests

**Backend** (81 tests; Docker must be running, because the tests start their own throwaway MongoDB):

```bash
dotnet test backend
```

**Frontend** (28 tests):

```bash
npm --prefix frontend test
```

The backend tests cover the API end to end (registration, access rules, search, rating statistics under
concurrent writes, recommendations), the database rules (unique ratings, indexes, the time-series
collection), the recommendation algorithm on hand-made data, and the importer's file parsing.

## Project layout

```
backend/
  src/CineMatch.Api/              HTTP API: controllers, login tokens, API documentation
  src/CineMatch.Core/             data models and interfaces (no dependencies)
  src/CineMatch.Infrastructure/   MongoDB access: repositories, indexes
  src/CineMatch.Recommender/      collaborative filtering: similarity build, scoring, accuracy check
  tools/CineMatch.Importer/       loads MovieLens (and TMDB) data
  tools/CineMatch.Perf/           measures response times and query plans
  tests/CineMatch.Tests/
frontend/                         the Angular site (see frontend/README.md)
docs/
  openapi.json                    the API description
  CineMatch.postman_collection.json   31 ready-made requests
  recommender-evaluation.md       accuracy results and how the settings were chosen
perf/results/                     performance measurements (summary.md, CSV, raw JSON)
docker-compose.yml                MongoDB, Redis; with --profile app also the API and the site
```

## How it works

### Data model (MongoDB)

| Collection | One document is | Notes |
|---|---|---|
| `users` | an account with its profile | unique `username` and `email` |
| `movies` | a catalog item | carries its own rating count, sum and average |
| `ratings` | one user's rating of one movie | unique per user and movie |
| `events` | a view of a movie | a time-series collection |
| `watchlist` | a movie a user saved | unique per user and movie |
| `item_similarities` | the 50 movies most similar to one movie | rebuilt offline |
| `counters` | the next free integer id | for users and movies created through the API |

Design choices:

- **Ratings are a collection of their own**, not an array inside the user. A user's ratings grow without
  limit, MongoDB documents are capped at 16 MB, and the recommender needs ratings both by user and by movie.
- **Each movie stores its rating statistics.** Saving a rating updates the rating and the movie's
  statistics in one transaction, so lists can sort by rating or popularity through an index instead of
  aggregating millions of ratings on every request.
- **Views go to a time-series collection**: they are append-only, time-ordered and high-volume.
- **Similar movies are computed ahead of time.** The expensive comparison of all movies runs offline;
  a recommendation request then needs only two indexed lookups.
- **Every list query is backed by an index** (see `IndexInitializer.cs`), including a text index over
  titles and tags for search.

### Recommendations

1. **Offline: similarity.** For every pair of movies rated by at least 5 common users, the adjusted
   cosine similarity is computed (each rating minus that user's average), scored down when few users
   rated both. Each movie keeps its 50 most similar movies in `item_similarities`.
2. **Online: scoring.** For a user, every movie they rated passes its opinion to its similar movies:
   liked movies push them up, disliked ones push them down. The best unseen movies are returned, each
   with the predicted rating and the rated movie that led to it.
3. **New users** (fewer than 5 ratings) get well-rated popular movies, from their favorite genres first.

Measured on held-out ratings, the predictions beat the "user's average" baseline and the lists beat
"most popular movies"; the numbers and the tuning are in
[docs/recommender-evaluation.md](docs/recommender-evaluation.md).

## API

Interactive documentation: http://localhost:5196/scalar/v1 while the API runs. The same description is
in [docs/openapi.json](docs/openapi.json), and [docs/CineMatch.postman_collection.json](docs/CineMatch.postman_collection.json)
has a request for every endpoint.

Requests marked *login* need the header `Authorization: Bearer <token>`, where the token comes from
`/api/auth/login` or `/api/auth/register`. Requests marked *admin* need an administrator's token.

| Method and path | Access | Purpose |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | open | Create an account; sign in |
| `GET /api/users/me`, `PUT /api/users/me` | login | Read and edit the profile |
| `GET /api/movies` | open | Search and browse: `q`, `genre`, `yearFrom`, `yearTo`, `minRatings`, `sort`, `page`, `pageSize` |
| `GET /api/movies/{id}` | open | One movie; with a token also the caller's rating and watchlist state |
| `GET /api/genres` | open | All genres |
| `POST /api/movies`, `PUT /api/movies/{id}`, `DELETE /api/movies/{id}` | admin | Manage the catalog |
| `PUT /api/movies/{id}/rating`, `DELETE /api/movies/{id}/rating` | login | Rate (0.5 to 5 in half steps); remove a rating |
| `POST /api/movies/{id}/views` | login | Record a view |
| `GET /api/users/me/history` | login | The caller's ratings or views (`type=ratings` or `views`) |
| `GET /api/users/me/watchlist`, `POST` and `DELETE /api/users/me/watchlist/{movieId}` | login | The watchlist |
| `GET /api/recommendations/me` | login | Personal recommendations |
| `GET /api/movies/{id}/similar` | open | Movies similar to one movie |
| `GET /api/movies/popular` | open | Best-rated movies, overall or for one `genre` |
| `GET /api/admin/recommender/status` | admin | State of the similarity table |
| `POST /api/admin/recommender/rebuild` | admin | Rebuild the similarity table (`wait=true` to wait for it) |
| `POST /api/admin/recommender/evaluate` | admin | Measure recommendation accuracy |
| `GET /health` | open | Is the API up and connected to MongoDB |

## Performance measurements

`tools/CineMatch.Perf` loads a dataset, times every kind of request against the running API, and records
how MongoDB executed the main queries. Results are written to [perf/results](perf/results), with a
readable summary in `summary.md`.

```bash
dotnet run -c Release --project backend/tools/CineMatch.Perf -- prepare --dataset 1m
```

```bash
dotnet run -c Release --project backend/tools/CineMatch.Perf -- run --stage indexes
```

`prepare` **replaces the database content**. `run` needs the API to be running. To measure the baseline
without indexes, run `indexes drop`, then `run --stage no-indexes`, then `indexes create`. All commands
and options: `dotnet run --project backend/tools/CineMatch.Perf -- --help`.

## Status

Working and tested: the whole backend, the recommender, the site, and the Docker setup.

Not done yet:

- **Caching.** Redis is started by Docker Compose but nothing uses it yet.
- **Measurements on large data.** Only the `small` dataset has been measured, with and without indexes.
- **Posters and real descriptions** need a TMDB token (see [Loading the data](#loading-the-data));
  that import step is written but has not been run.
- **Datasets `10m` and `32m`** have not been imported yet.

## Troubleshooting

| Problem | Cause and fix |
|---|---|
| `failed to connect to the docker API` | Docker Desktop is not running. Start it and wait until it is ready |
| The API stops at once with `Jwt:SigningKey is missing` | Set the secret ([from source](#running-from-source)) or `JWT_SIGNING_KEY` in `.env` (Docker) |
| The importer says `Set a demo password` | Set `Importer:DemoPassword` (step 2 of [Running from source](#running-from-source)) |
| The importer says the database already has movies | Add `--reset` to replace the data |
| *For You* and *similar movies* show only popular movies | The similarity table is empty: restart the API or use *Recommender → Rebuild now* |
| `address already in use` on port 5196 | The API runs twice (container and source). Stop one of them |
| The site shows "Cannot reach the server" | The API is not running, or still starting |
| Cannot sign in as `user1` after re-importing | The accounts were recreated with the current `Importer:DemoPassword` |

## Data sources

- **MovieLens** ratings, movies and tags: F. Maxwell Harper and Joseph A. Konstan. 2015. *The MovieLens
  Datasets: History and Context.* ACM Transactions on Interactive Intelligent Systems 5, 4.
  The data is downloaded by the importer and is not part of this repository.
- **Posters and descriptions** (optional): this product uses the TMDB API but is not endorsed or
  certified by TMDB.
