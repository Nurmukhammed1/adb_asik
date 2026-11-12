import { Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Observable, map, of } from 'rxjs';
import { Api } from '../core/api';
import { AuthStore } from '../core/auth';
import { Recommendation } from '../core/models';
import { LoadState } from '../shared/load-state';
import { MovieCard } from '../shared/movie-card';

/** The start page: a short introduction, a taste of the personal recommendations, and the top-rated movies. */
@Component({
  selector: 'app-home',
  imports: [RouterLink, MatButtonModule, MatChipsModule, MatIconModule, MovieCard, LoadState],
  template: `
    <section class="hero">
      <div class="hero-inner">
        <h1>Find your next favorite movie</h1>
        <p>
          CineMatch learns from the ratings of people with the same taste as you and suggests what to watch next.
        </p>
        <div class="actions">
          @if (auth.isLoggedIn()) {
            <a matButton="filled" routerLink="/for-you">
              <mat-icon aria-hidden="true">auto_awesome</mat-icon>
              See your recommendations
            </a>
          } @else {
            <a matButton="filled" routerLink="/register">Create a free account</a>
          }
          <a matButton="outlined" routerLink="/movies">Browse the catalog</a>
        </div>
      </div>
    </section>

    <div class="page">
      @if (auth.isLoggedIn()) {
        <div class="heading">
          <h2 class="section-title">Recommended for you</h2>
          <a matButton routerLink="/for-you">See all</a>
        </div>
        <app-load-state [loading]="forYou.isLoading()" [error]="forYou.error()" (retry)="forYou.reload()" />
        @if (forYou.hasValue() && !forYou.isLoading()) {
          <div class="movie-grid">
            @for (item of forYou.value(); track item.movie.id) {
              <app-movie-card [movie]="item.movie" />
            }
          </div>
        }
      }

      <div class="heading">
        <h2 class="section-title">Top rated{{ genre() ? ' in ' + genre() : '' }}</h2>
      </div>

      @if (genres.hasValue()) {
        <mat-chip-listbox class="genres" aria-label="Genre" [value]="genre()" (change)="genre.set($event.value ?? null)">
          <mat-chip-option [value]="null">All</mat-chip-option>
          @for (name of genres.value(); track name) {
            <mat-chip-option [value]="name">{{ name }}</mat-chip-option>
          }
        </mat-chip-listbox>
      }

      <app-load-state
        [loading]="popular.isLoading()"
        [error]="popular.error()"
        [empty]="popular.hasValue() && popular.value().length === 0"
        emptyText="No rated movies yet."
        (retry)="popular.reload()"
      />
      @if (popular.hasValue() && !popular.isLoading()) {
        <div class="movie-grid">
          @for (item of popular.value(); track item.movie.id) {
            <app-movie-card [movie]="item.movie" />
          }
        </div>
      }
    </div>
  `,
  styles: `
    .hero {
      background: linear-gradient(135deg, var(--mat-sys-primary-container), var(--mat-sys-surface-container-low) 70%);
      color: var(--mat-sys-on-surface);
    }
    .hero-inner {
      box-sizing: border-box;
      max-width: var(--app-page-width);
      margin: 0 auto;
      padding: 56px 16px;
    }
    .hero h1 {
      font: var(--mat-sys-display-small);
      margin: 0 0 12px;
    }
    .hero p {
      font: var(--mat-sys-title-medium);
      color: var(--mat-sys-on-surface-variant);
      max-width: 56ch;
      margin: 0 0 24px;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
    .heading {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 12px;
    }
    .heading:first-child .section-title {
      margin-top: 8px;
    }
    .genres {
      display: block;
      margin-bottom: 20px;
    }
  `,
})
export class Home {
  private readonly api = inject(Api);
  protected readonly auth = inject(AuthStore);

  /** The genre chosen for the top-rated list; null means all genres. */
  protected readonly genre = signal<string | null>(null);

  protected readonly genres = rxResource({ stream: () => this.api.genres() });

  protected readonly popular = rxResource({
    params: () => this.genre(),
    stream: ({ params }) => this.api.popular(params, 12),
  });

  /** Six personal picks for logged-in visitors; the full list is on the For You page. */
  protected readonly forYou = rxResource({
    params: () => this.auth.isLoggedIn(),
    stream: ({ params }): Observable<Recommendation[]> =>
      params ? this.api.recommendations(6).pipe(map((list) => list.items)) : of([]),
  });
}
