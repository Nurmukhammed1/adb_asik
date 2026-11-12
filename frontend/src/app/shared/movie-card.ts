import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { MovieSummary } from '../core/models';
import { Poster } from './poster';

/** One movie in a grid: poster, title, year and average rating. Extra lines can be projected below. */
@Component({
  selector: 'app-movie-card',
  imports: [RouterLink, MatIconModule, DecimalPipe, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="poster-link" [routerLink]="['/movies', movie().id]" [attr.aria-label]="movie().title">
      <app-poster [movie]="movie()" />
    </a>
    <a class="title" [routerLink]="['/movies', movie().id]" [title]="movie().title">{{ movie().title }}</a>
    <div class="meta">
      <span>{{ movie().year ?? 'Year unknown' }}</span>
      @if (movie().ratingStats.count > 0) {
        <span class="rating" [title]="movie().ratingStats.count + ' ratings'">
          <mat-icon class="filled" aria-hidden="true">star</mat-icon>
          {{ movie().ratingStats.avg | number: '1.1-1' }}
          <span class="count">({{ movie().ratingStats.count | number }})</span>
        </span>
      } @else {
        <span class="count">No ratings</span>
      }
    </div>
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 0;
    }
    .poster-link {
      display: block;
      border-radius: 12px;
      transition: transform 120ms ease;
    }
    .poster-link:hover,
    .poster-link:focus-visible {
      transform: translateY(-3px);
    }
    .title {
      font: var(--mat-sys-title-small);
      color: var(--mat-sys-on-surface);
      text-decoration: none;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .title:hover {
      text-decoration: underline;
    }
    .meta {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
    .rating {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      color: var(--mat-sys-on-surface);
      white-space: nowrap;
    }
    .rating mat-icon {
      font-size: 16px;
      width: 16px;
      height: 16px;
      color: var(--app-star);
    }
    .count {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class MovieCard {
  readonly movie = input.required<MovieSummary>();
}
