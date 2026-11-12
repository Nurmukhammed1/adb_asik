# CineMatch web app

The site for the CineMatch API: browse and search movies, rate them, keep a watchlist, and get
personal recommendations. Built with Angular 22 (standalone components, signals) and Angular Material.

## Run it

The site needs the API, and the API needs MongoDB.

**From source** (three terminals, from the repository root):

```bash
docker compose up -d
dotnet run --project backend/src/CineMatch.Api --launch-profile http
npm --prefix frontend start
```

Then open http://localhost:4200. The dev server passes every `/api` request on to the API at
`http://localhost:5196` (see `proxy.conf.json`), so the API needs no CORS setup.

**Everything in Docker** (one command, from the repository root; needs a `.env` file, see `.env.example`):

```bash
docker compose --profile app up -d --build
```

Then open http://localhost:8080. nginx serves the built site and passes `/api` on to the API container.

To sign in, create an account on the site, or use an imported MovieLens account (`user1`, `user2`, ...)
or `admin` with the importer's demo password.

## Commands

Run these inside `frontend/`.

| Command | What it does |
|---|---|
| `npm start` | Development server on port 4200 with live reload |
| `npm test` | Unit tests (Vitest), run once |
| `npm run build` | Production build into `dist/cinematch/browser` |

## Pages

| Address | Who | What |
|---|---|---|
| `/` | everyone | Top-rated movies, overall or by genre; a preview of the personal recommendations |
| `/movies` | everyone | Catalog with search, genre, year and rating-count filters, sorting and pages. The filters live in the address, so a view can be shared |
| `/movies/:id` | everyone | Details, average rating, similar movies; rating and watchlist for signed-in users |
| `/for-you` | signed in | Personal recommendations with the reason for each one |
| `/profile` | signed in | Display name and favorite genres, rating history, viewed movies, watchlist |
| `/login`, `/register` | everyone | Sign in and sign up |
| `/admin/movies` | admin | Add, edit and delete movies |
| `/admin/recommender` | admin | State of the similarity table, rebuild, accuracy check |

## Code layout

```
src/app/
  app.ts, app.html, app.scss   the frame: top bar, account menu, footer
  app.routes.ts                all pages, each loaded on demand
  app.config.ts                router, HTTP client, Material defaults
  core/
    models.ts                  the shapes the API sends and accepts (mirrors docs/openapi.json)
    api.ts                     every API call, and turning a failed request into a message
    auth.ts                    the login session, the token interceptor, the page guards
  shared/                      poster, movie card, star rating, loading/error/empty block, confirm dialog
  features/                    one file per page
```

## How it works

- **Session.** The login token is kept in `localStorage` with its expiry time (60 minutes). The
  interceptor adds it to API requests; when the API answers 401 the session ends and the user is sent
  to the sign-in page, then back to where they were.
- **Loading data.** Pages load data with `rxResource`, which re-runs when its parameters change
  (a filter, a page number, the movie id) and exposes loading, error and value states to the template.
- **Posters.** Movies have no poster image until the importer is run with a TMDB token; until then
  each movie gets a colored card with its title.
