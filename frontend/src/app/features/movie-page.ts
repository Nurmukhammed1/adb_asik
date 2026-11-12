import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, linkedSignal, numberAttribute, signal, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { AuthStore } from '../core/auth';
import { LoadState } from '../shared/load-state';
import { MovieCard } from '../shared/movie-card';
import { Poster } from '../shared/poster';
import { StarRating } from '../shared/star-rating';

/** One movie: details, the visitor's own rating and watchlist button, and similar movies. */
@Component({
  selector: 'app-movie-page',
  imports: [RouterLink, DecimalPipe, MatButtonModule, MatChipsModule, MatIconModule, Poster, StarRating, MovieCard, LoadState],
  template: `
    <div class="page">
      <app-load-state [loading]="details.isLoading()" [error]="details.error()" (retry)="details.reload()" />

      @if (movie(); as movie) {
        <div class="layout">
          <app-poster class="poster" [movie]="movie" />

          <div class="info">
            <h1 class="page-title">{{ movie.title }}</h1>
            <p class="muted year">{{ movie.year ?? 'Year unknown' }}</p>

            <mat-chip-set aria-label="Genres">
              @for (genre of movie.genres; track genre) {
                <a mat-chip routerLink="/movies" [queryParams]="{ genre }">{{ genre }}</a>
              }
            </mat-chip-set>

            @if (stats(); as stats) {
              <div class="average" [attr.aria-label]="'Average rating ' + (stats.avg | number: '1.1-1') + ' out of 5'">
                @if (stats.count > 0) {
                  <span class="number">{{ stats.avg | number: '1.1-1' }}</span>
                  <div>
                    <app-star-rating class="small" [value]="roundToHalf(stats.avg)" [readonly]="true" />
                    <div class="muted">{{ stats.count | number }} {{ stats.count === 1 ? 'rating' : 'ratings' }}</div>
                  </div>
                } @else {
                  <span class="muted">No ratings yet. Be the first to rate it.</span>
                }
              </div>
            }

            <div class="surface-card mine">
              @if (auth.isLoggedIn()) {
                <div class="label">{{ myRating() === null ? 'Rate this movie' : 'Your rating: ' + myRating() }}</div>
                <div class="rate-row">
                  <app-star-rating [value]="myRating()" [readonly]="busy()" (rated)="rate($event)" />
                  @if (myRating() !== null) {
                    <button matButton type="button" [disabled]="busy()" (click)="removeRating()">Remove rating</button>
                  }
                </div>
                <button matButton="tonal" type="button" [disabled]="busy()" (click)="toggleWatchlist()">
                  <mat-icon [class.filled]="inWatchlist()" aria-hidden="true">bookmark</mat-icon>
                  {{ inWatchlist() ? 'In your watchlist' : 'Add to watchlist' }}
                </button>
              } @else {
                <div class="label">Seen it?</div>
                <a matButton="filled" routerLink="/login" [queryParams]="{ returnUrl: '/movies/' + movie.id }">
                  Sign in to rate and save movies
                </a>
              }
            </div>

            <h2 class="section-title">About</h2>
            <p class="description">{{ movie.description }}</p>

            @if (movie.tags.length > 0) {
              <mat-chip-set aria-label="Tags">
                @for (tag of movie.tags; track tag) {
                  <a mat-chip routerLink="/movies" [queryParams]="{ q: tag }">{{ tag }}</a>
                }
              </mat-chip-set>
            }
          </div>
        </div>

        <h2 class="section-title">{{ similarIsFallback() ? 'Popular in the same genres' : 'People who liked this also liked' }}</h2>
        <app-load-state
          [loading]="similar.isLoading()"
          [error]="similar.error()"
          [empty]="similar.hasValue() && similar.value().length === 0"
          emptyText="Not enough ratings yet to find similar movies."
          (retry)="similar.reload()"
        />
        @if (similar.hasValue() && !similar.isLoading()) {
          <div class="movie-grid">
            @for (item of similar.value(); track item.movie.id) {
              <app-movie-card [movie]="item.movie" />
            }
          </div>
        }
      }
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(180px, 280px) 1fr;
      gap: 32px;
      align-items: start;
    }
    .year {
      margin: 0 0 12px;
      font: var(--mat-sys-title-medium);
    }
    .average {
      display: flex;
      align-items: center;
      gap: 16px;
      margin: 20px 0;
    }
    .average .number {
      font: var(--mat-sys-display-small);
    }
    .small {
      --star-size: 20px;
    }
    .mine {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 12px;
      max-width: 420px;
    }
    .label {
      font: var(--mat-sys-title-medium);
    }
    .rate-row {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px;
    }
    .description {
      max-width: 70ch;
      line-height: 1.6;
      margin: 0 0 16px;
    }
    @media (max-width: 700px) {
      .layout {
        grid-template-columns: 1fr;
      }
      .poster {
        max-width: 220px;
      }
    }
  `,
})
export class MoviePage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly title = inject(Title);
  protected readonly auth = inject(AuthStore);

  /** The :id part of the address. */
  readonly id = input.required({ transform: numberAttribute });

  protected readonly details = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.movie(params),
  });

  protected readonly similar = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.similar(params, 12),
  });

  protected readonly movie = computed(() => (this.details.hasValue() ? this.details.value().movie : null));

  // These start from what the server sent and are updated in place after each action,
  // so the page reflects a new rating at once without loading the movie again.
  protected readonly stats = linkedSignal(() => this.movie()?.ratingStats ?? null);
  protected readonly myRating = linkedSignal(() => (this.details.hasValue() ? this.details.value().myRating : null));
  protected readonly inWatchlist = linkedSignal(() => (this.details.hasValue() ? this.details.value().inWatchlist : false));

  protected readonly busy = signal(false);

  /** A movie with too few ratings has no neighbors; the API then sends popular movies of its genres. */
  protected readonly similarIsFallback = computed(
    () => this.similar.hasValue() && this.similar.value().some((item) => item.source === 'popular'),
  );

  constructor() {
    // Opening a movie page counts as a view (for logged-in users).
    effect(() => {
      const id = this.id();
      if (untracked(() => this.auth.isLoggedIn()) && Number.isFinite(id)) {
        this.api.recordView(id).subscribe({ error: () => undefined });
      }
    });

    effect(() => {
      const movie = this.movie();
      if (movie) this.title.setTitle(`${movie.title} · CineMatch`);
    });
  }

  protected roundToHalf(value: number): number {
    return Math.round(value * 2) / 2;
  }

  protected rate(rating: number): void {
    this.busy.set(true);
    this.api.rate(this.id(), rating).subscribe({
      next: (response) => {
        this.myRating.set(response.rating);
        this.stats.set(response.ratingStats);
        this.busy.set(false);
        this.snackBar.open(`Rated ${rating} out of 5`, 'OK');
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected removeRating(): void {
    this.busy.set(true);
    this.api.removeRating(this.id()).subscribe({
      next: (response) => {
        this.myRating.set(null);
        this.stats.set(response.ratingStats);
        this.busy.set(false);
        this.snackBar.open('Rating removed', 'OK');
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected toggleWatchlist(): void {
    const add = !this.inWatchlist();
    this.busy.set(true);
    const request = add ? this.api.addToWatchlist(this.id()) : this.api.removeFromWatchlist(this.id());
    request.subscribe({
      next: () => {
        this.inWatchlist.set(add);
        this.busy.set(false);
        const message = this.snackBar.open(add ? 'Added to your watchlist' : 'Removed from your watchlist', add ? 'View' : 'OK');
        if (add) message.onAction().subscribe(() => void this.router.navigate(['/profile'], { queryParams: { tab: 'watchlist' } }));
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.busy.set(false);
    this.snackBar.open(errorMessage(error), 'OK');
  }
}
