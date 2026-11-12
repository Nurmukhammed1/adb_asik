import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject } from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, ParamMap, Params, Router } from '@angular/router';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { Api } from '../core/api';
import { MovieQuery, MovieSort } from '../core/models';
import { LoadState } from '../shared/load-state';
import { MovieCard } from '../shared/movie-card';

const PageSize = 24;
const Sorts: MovieSort[] = ['relevance', 'popularity', 'rating', 'year', 'title'];

function numberOrUndefined(value: string | null): number | undefined {
  const number = value === null || value.trim() === '' ? NaN : Number(value);
  return Number.isFinite(number) ? number : undefined;
}

/**
 * The catalog: search, filters, sorting and pages. All of it lives in the address bar
 * (?q=...&genre=...&page=2), so a view can be reloaded, bookmarked or shared.
 */
@Component({
  selector: 'app-catalog',
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MovieCard,
    LoadState,
  ],
  template: `
    <div class="page">
      <h1 class="page-title">Browse movies</h1>
      <p class="page-subtitle">
        @if (movies.hasValue()) {
          {{ movies.value().total | number }}
          @if (hasFilters()) {
            {{ movies.value().total === 1 ? 'movie matches' : 'movies match' }} your filters
          } @else {
            {{ movies.value().total === 1 ? 'movie' : 'movies' }}
          }
        } @else {
          &nbsp;
        }
      </p>

      <div class="filters">
        <mat-form-field class="search" subscriptSizing="dynamic">
          <mat-label>Search titles and tags</mat-label>
          <mat-icon matPrefix aria-hidden="true">search</mat-icon>
          <input matInput type="search" [formControl]="search" />
        </mat-form-field>

        <mat-form-field subscriptSizing="dynamic">
          <mat-label>Genre</mat-label>
          <mat-select [value]="query().genre ?? ''" (selectionChange)="change({ genre: $event.value || null })">
            <mat-option value="">All genres</mat-option>
            @if (genres.hasValue()) {
              @for (genre of genres.value(); track genre) {
                <mat-option [value]="genre">{{ genre }}</mat-option>
              }
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field class="year" subscriptSizing="dynamic">
          <mat-label>From year</mat-label>
          <input
            matInput
            type="number"
            min="1870"
            max="2100"
            [value]="query().yearFrom ?? ''"
            (change)="change({ yearFrom: $any($event.target).value || null })"
          />
        </mat-form-field>

        <mat-form-field class="year" subscriptSizing="dynamic">
          <mat-label>To year</mat-label>
          <input
            matInput
            type="number"
            min="1870"
            max="2100"
            [value]="query().yearTo ?? ''"
            (change)="change({ yearTo: $any($event.target).value || null })"
          />
        </mat-form-field>

        <mat-form-field subscriptSizing="dynamic">
          <mat-label>Ratings</mat-label>
          <mat-select [value]="query().minRatings ?? 0" (selectionChange)="change({ minRatings: $event.value || null })">
            <mat-option [value]="0">Any number</mat-option>
            <mat-option [value]="10">10 or more</mat-option>
            <mat-option [value]="50">50 or more</mat-option>
            <mat-option [value]="100">100 or more</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field subscriptSizing="dynamic">
          <mat-label>Sort by</mat-label>
          <mat-select [value]="sort()" (selectionChange)="change({ sort: $event.value })">
            @if (query().q) {
              <mat-option value="relevance">Best match</mat-option>
            }
            <mat-option value="popularity">Most rated</mat-option>
            <mat-option value="rating">Best rated</mat-option>
            <mat-option value="year">Newest</mat-option>
            <mat-option value="title">Title A–Z</mat-option>
          </mat-select>
        </mat-form-field>

        @if (hasFilters()) {
          <button matButton type="button" (click)="clear()">
            <mat-icon aria-hidden="true">filter_alt_off</mat-icon>
            Clear filters
          </button>
        }
      </div>

      <app-load-state
        [loading]="movies.isLoading()"
        [error]="movies.error()"
        [empty]="movies.hasValue() && movies.value().items.length === 0"
        emptyText="No movies match. Try another word or fewer filters."
        emptyIcon="search_off"
        (retry)="movies.reload()"
      />

      @if (movies.hasValue() && !movies.isLoading() && movies.value().items.length > 0) {
        <div class="movie-grid">
          @for (movie of movies.value().items; track movie.id) {
            <app-movie-card [movie]="movie" />
          }
        </div>

        <mat-paginator
          aria-label="Pages of movies"
          [length]="movies.value().total"
          [pageSize]="pageSize"
          [pageIndex]="movies.value().page - 1"
          [hidePageSize]="true"
          [showFirstLastButtons]="true"
          (page)="change({ page: $event.pageIndex + 1 })"
        />
      }
    </div>
  `,
  styles: `
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-bottom: 24px;
    }
    .filters mat-form-field {
      width: 170px;
    }
    .filters .search {
      flex: 1 1 260px;
    }
    .filters .year {
      width: 120px;
    }
    mat-paginator {
      margin-top: 24px;
      background: transparent;
    }
  `,
})
export class Catalog {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly pageSize = PageSize;
  protected readonly search = new FormControl('', { nonNullable: true });

  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });

  /** The address bar, read as a query for the API. */
  protected readonly query = computed<MovieQuery>(() => {
    const p: ParamMap = this.params();
    const sort = p.get('sort') as MovieSort | null;
    return {
      q: p.get('q')?.trim() || undefined,
      genre: p.get('genre') || undefined,
      yearFrom: numberOrUndefined(p.get('yearFrom')),
      yearTo: numberOrUndefined(p.get('yearTo')),
      minRatings: numberOrUndefined(p.get('minRatings')),
      sort: sort && Sorts.includes(sort) ? sort : undefined,
      page: Math.max(1, numberOrUndefined(p.get('page')) ?? 1),
      pageSize: PageSize,
    };
  });

  /** Without an explicit choice: best match while searching, most rated otherwise. */
  protected readonly sort = computed<MovieSort>(() => {
    const query = this.query();
    if (query.sort && (query.sort !== 'relevance' || query.q)) return query.sort;
    return query.q ? 'relevance' : 'popularity';
  });

  protected readonly hasFilters = computed(() => {
    const { q, genre, yearFrom, yearTo, minRatings } = this.query();
    return Boolean(q || genre || yearFrom || yearTo || minRatings);
  });

  protected readonly movies = rxResource({
    params: () => this.query(),
    stream: ({ params }) => this.api.searchMovies(params),
  });

  protected readonly genres = rxResource({ stream: () => this.api.genres() });

  constructor() {
    // Typing searches after a short pause, not on every key.
    this.search.valueChanges
      .pipe(
        debounceTime(300),
        map((text) => text.trim()),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((q) => {
        if (q !== (this.query().q ?? '')) this.change({ q: q || null });
      });

    // The search box follows the address bar (for example after a search from the top bar).
    effect(() => {
      const q = this.query().q ?? '';
      if (this.search.value.trim() !== q) this.search.setValue(q, { emitEvent: false });
    });
  }

  /** Applies filter changes. Any change other than the page number goes back to page 1. */
  protected change(changes: Params): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: null, ...changes },
      queryParamsHandling: 'merge',
    });
  }

  protected clear(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }
}
