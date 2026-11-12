// The shapes the CineMatch API sends and accepts. They mirror docs/openapi.json.

export interface RatingStats {
  count: number;
  sum: number;
  avg: number;
}

/** What list views get: no description or tags. */
export interface MovieSummary {
  id: number;
  title: string;
  year: number | null;
  genres: string[];
  posterUrl: string | null;
  ratingStats: RatingStats;
}

export interface Movie extends MovieSummary {
  description: string;
  tags: string[];
  tmdbId: number | null;
}

/** A movie plus, for a logged-in caller, their own rating and watchlist state. */
export interface MovieDetails {
  movie: Movie;
  myRating: number | null;
  inWatchlist: boolean;
}

export interface MovieInput {
  title: string;
  year: number | null;
  genres: string[];
  description: string;
  tags: string[];
  posterUrl: string | null;
  tmdbId: number | null;
}

export type MovieSort = 'relevance' | 'rating' | 'popularity' | 'year' | 'title';

export interface MovieQuery {
  q?: string;
  genre?: string;
  yearFrom?: number;
  yearTo?: number;
  minRatings?: number;
  sort?: MovieSort;
  page?: number;
  pageSize?: number;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface RatingResponse {
  rating: number | null;
  ratingStats: RatingStats;
}

export interface UserProfile {
  displayName: string;
  favoriteGenres: string[];
}

export interface User {
  id: number;
  username: string;
  email: string;
  role: 'user' | 'admin';
  profile: UserProfile;
  createdAt: string;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: User;
}

export type HistoryType = 'ratings' | 'views';

export interface HistoryItem {
  type: 'rating' | 'view';
  movie: MovieSummary;
  rating: number | null;
  ts: string;
}

export interface WatchlistEntry {
  movie: MovieSummary;
  addedAt: string;
}

/**
 * collaborative: personal, score is the predicted rating.
 * popular: well rated by many, score is the weighted average rating.
 * similar: a stored neighbor of one movie, score is the similarity from 0 to 1.
 */
export type RecommendationSource = 'collaborative' | 'popular' | 'similar';

export interface Recommendation {
  movie: MovieSummary;
  source: RecommendationSource;
  score: number;
  becauseOf: MovieSummary | null;
}

export interface RecommendationList {
  strategy: 'collaborative' | 'popular';
  items: Recommendation[];
}

export interface RebuildResult {
  startedAt: string;
  users: number;
  movies: number;
  ratings: number;
  moviesWithNeighbors: number;
  storedPairs: number;
  loadSeconds: number;
  buildSeconds: number;
  saveSeconds: number;
}

export interface RecommenderSettings {
  neighborsPerMovie: number;
  minCommonRaters: number;
  shrinkage: number;
  minRatingsForPersonal: number;
  predictionDamping: number;
  rankBy: 'evidence' | 'predictedRating';
  popularPriorWeight: number;
  nightlyRebuildHour: number | null;
  buildOnStartupIfEmpty: boolean;
}

export interface RecommenderStatus {
  running: boolean;
  moviesWithNeighbors: number;
  lastRebuild: RebuildResult | null;
  lastError: string | null;
  settings: RecommenderSettings;
}

export interface TopNQuality {
  precision: number;
  recall: number;
  catalogCoverage: number;
}

export interface EvaluationReport {
  users: number;
  movies: number;
  trainRatings: number;
  testRatings: number;
  testFraction: number;
  similarityBuildSeconds: number;
  rmseItemBased: number;
  rmseUserAverage: number;
  rmseGlobalAverage: number;
  predictionCoverage: number;
  listSize: number;
  likedThreshold: number;
  topNUsers: number;
  itemBasedByEvidence: TopNQuality;
  itemBasedByPredictedRating: TopNQuality;
  mostPopular: TopNQuality;
}

/** The error body the API sends (RFC 9457). Validation errors list the bad fields. */
export interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
