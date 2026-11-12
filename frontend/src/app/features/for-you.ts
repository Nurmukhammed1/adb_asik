import { DecimalPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { LoadState } from '../shared/load-state';
import { MovieCard } from '../shared/movie-card';

/**
 * Personal recommendations. Each card says why it is there: the predicted rating and the movie
 * the visitor rated that led to it, or "popular" while the visitor has too few ratings.
 */
@Component({
  selector: 'app-for-you',
  imports: [RouterLink, DecimalPipe, MatButtonModule, MatIconModule, MovieCard, LoadState],
  template: `
    <div class="page">
      <h1 class="page-title">For You</h1>
      <p class="page-subtitle">
        @if (list.hasValue() && list.value().strategy === 'collaborative') {
          Picked from the ratings of people who liked the same movies as you.
        } @else {
          Movies you might like.
        }
      </p>

      <app-load-state
        [loading]="list.isLoading()"
        [error]="list.error()"
        [empty]="list.hasValue() && list.value().items.length === 0"
        emptyText="No recommendations yet. Rate a few movies first."
        (retry)="list.reload()"
      />

      @if (list.hasValue() && !list.isLoading()) {
        @if (list.value().strategy === 'popular') {
          <div class="surface-card notice">
            <mat-icon aria-hidden="true">lightbulb</mat-icon>
            <div>
              <strong>These are popular picks, not personal ones yet.</strong>
              <p>
                Rate at least five movies and this page starts to follow your own taste. Choosing favorite genres
                in your profile already tunes the picks below.
              </p>
              <div class="notice-actions">
                <a matButton="filled" routerLink="/movies">Find movies to rate</a>
                <a matButton="outlined" routerLink="/profile">Choose favorite genres</a>
              </div>
            </div>
          </div>
        }

        <div class="movie-grid">
          @for (item of list.value().items; track item.movie.id) {
            <app-movie-card [movie]="item.movie">
              @if (item.source === 'collaborative') {
                <div class="why">
                  <span class="predicted" title="The rating we expect you to give">
                    <mat-icon aria-hidden="true">auto_awesome</mat-icon>
                    {{ item.score | number: '1.1-1' }} predicted
                  </span>
                  @if (item.becauseOf; as source) {
                    <span>
                      Because you liked
                      <a [routerLink]="['/movies', source.id]">{{ source.title }}</a>
                    </span>
                  }
                </div>
              } @else {
                <div class="why">
                  <span class="predicted">
                    <mat-icon aria-hidden="true">trending_up</mat-icon>
                    Popular
                  </span>
                </div>
              }
            </app-movie-card>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .notice {
      display: flex;
      gap: 16px;
      margin-bottom: 24px;
    }
    .notice > mat-icon {
      color: var(--mat-sys-tertiary);
      flex: none;
    }
    .notice p {
      margin: 6px 0 14px;
      color: var(--mat-sys-on-surface-variant);
      max-width: 64ch;
    }
    .notice-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .why {
      display: flex;
      flex-direction: column;
      gap: 2px;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow-wrap: anywhere;
    }
    .predicted {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      color: var(--mat-sys-tertiary);
      font-weight: 500;
    }
    .predicted mat-icon {
      font-size: 16px;
      width: 16px;
      height: 16px;
    }
  `,
})
export class ForYou {
  private readonly api = inject(Api);

  protected readonly list = rxResource({ stream: () => this.api.recommendations(24) });
}
