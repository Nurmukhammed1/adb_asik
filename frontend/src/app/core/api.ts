import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AuthResponse,
  EvaluationReport,
  HistoryItem,
  HistoryType,
  Movie,
  MovieDetails,
  MovieInput,
  MovieQuery,
  MovieSummary,
  Paged,
  ProblemDetails,
  RatingResponse,
  RebuildResult,
  Recommendation,
  RecommendationList,
  RecommenderStatus,
  User,
  UserProfile,
  WatchlistEntry,
} from './models';

/** Every call the site makes to the CineMatch API. Paths are relative; the dev server and nginx proxy /api. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  // ---------- Auth and profile ----------

  register(body: { username: string; email: string; password: string; displayName?: string }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', body);
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', { username, password });
  }

  me(): Observable<User> {
    return this.http.get<User>('/api/users/me');
  }

  updateProfile(profile: UserProfile): Observable<User> {
    return this.http.put<User>('/api/users/me', profile);
  }

  // ---------- Catalog ----------

  searchMovies(query: MovieQuery): Observable<Paged<MovieSummary>> {
    return this.http.get<Paged<MovieSummary>>('/api/movies', { params: toParams(query) });
  }

  movie(id: number): Observable<MovieDetails> {
    return this.http.get<MovieDetails>(`/api/movies/${id}`);
  }

  genres(): Observable<string[]> {
    return this.http.get<string[]>('/api/genres');
  }

  createMovie(movie: MovieInput): Observable<Movie> {
    return this.http.post<Movie>('/api/movies', movie);
  }

  updateMovie(id: number, movie: MovieInput): Observable<Movie> {
    return this.http.put<Movie>(`/api/movies/${id}`, movie);
  }

  deleteMovie(id: number): Observable<void> {
    return this.http.delete<void>(`/api/movies/${id}`);
  }

  // ---------- Interactions ----------

  rate(movieId: number, rating: number): Observable<RatingResponse> {
    return this.http.put<RatingResponse>(`/api/movies/${movieId}/rating`, { rating });
  }

  removeRating(movieId: number): Observable<RatingResponse> {
    return this.http.delete<RatingResponse>(`/api/movies/${movieId}/rating`);
  }

  recordView(movieId: number): Observable<void> {
    return this.http.post<void>(`/api/movies/${movieId}/views`, null);
  }

  history(type: HistoryType, page: number, pageSize: number): Observable<Paged<HistoryItem>> {
    return this.http.get<Paged<HistoryItem>>('/api/users/me/history', { params: toParams({ type, page, pageSize }) });
  }

  watchlist(page: number, pageSize: number): Observable<Paged<WatchlistEntry>> {
    return this.http.get<Paged<WatchlistEntry>>('/api/users/me/watchlist', { params: toParams({ page, pageSize }) });
  }

  addToWatchlist(movieId: number): Observable<void> {
    return this.http.post<void>(`/api/users/me/watchlist/${movieId}`, null);
  }

  removeFromWatchlist(movieId: number): Observable<void> {
    return this.http.delete<void>(`/api/users/me/watchlist/${movieId}`);
  }

  // ---------- Recommendations ----------

  recommendations(limit: number): Observable<RecommendationList> {
    return this.http.get<RecommendationList>('/api/recommendations/me', { params: toParams({ limit }) });
  }

  similar(movieId: number, limit: number): Observable<Recommendation[]> {
    return this.http.get<Recommendation[]>(`/api/movies/${movieId}/similar`, { params: toParams({ limit }) });
  }

  popular(genre: string | null, limit: number): Observable<Recommendation[]> {
    return this.http.get<Recommendation[]>('/api/movies/popular', { params: toParams({ genre, limit }) });
  }

  // ---------- Recommender administration ----------

  recommenderStatus(): Observable<RecommenderStatus> {
    return this.http.get<RecommenderStatus>('/api/admin/recommender/status');
  }

  /** Waits until the rebuild has finished and returns what it processed. */
  rebuildSimilarities(): Observable<RebuildResult> {
    return this.http.post<RebuildResult>('/api/admin/recommender/rebuild', null, { params: toParams({ wait: true }) });
  }

  evaluateRecommender(): Observable<EvaluationReport> {
    return this.http.post<EvaluationReport>('/api/admin/recommender/evaluate', null);
  }
}

/** Builds query parameters, leaving out everything that is empty. */
function toParams(values: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
  }
  return params;
}

/** A sentence for the user from whatever a failed request produced. */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the server. Check that the API is running.';
    const problem = error.error as ProblemDetails | null;
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
    if (problem?.title) return problem.title;
    if (error.status === 404) return 'Not found.';
    if (error.status === 403) return 'You are not allowed to do this.';
    return `The request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}
