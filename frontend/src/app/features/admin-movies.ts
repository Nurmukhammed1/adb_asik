import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, numberAttribute, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { Api, errorMessage } from '../core/api';
import { MovieInput, MovieSummary } from '../core/models';
import { ConfirmData, ConfirmDialog, LoadState } from '../shared/load-state';

const PageSize = 20;

/** The catalog as a table, with search, and edit and delete for every movie. */
@Component({
  selector: 'app-admin-movies',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DecimalPipe,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    LoadState,
  ],
  template: `
    <div class="page">
      <div class="heading">
        <div>
          <h1 class="page-title">Manage movies</h1>
          <p class="page-subtitle">Add, edit and delete catalog entries.</p>
        </div>
        <a matButton="filled" routerLink="/admin/movies/new">
          <mat-icon aria-hidden="true">add</mat-icon>
          New movie
        </a>
      </div>

      <mat-form-field class="search" subscriptSizing="dynamic">
        <mat-label>Search titles and tags</mat-label>
        <mat-icon matPrefix aria-hidden="true">search</mat-icon>
        <input matInput type="search" [formControl]="search" />
      </mat-form-field>

      <app-load-state
        [loading]="movies.isLoading()"
        [error]="movies.error()"
        [empty]="movies.hasValue() && movies.value().items.length === 0"
        emptyText="No movies match."
        emptyIcon="search_off"
        (retry)="movies.reload()"
      />

      @if (movies.hasValue() && !movies.isLoading() && movies.value().items.length > 0) {
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th scope="col" class="number">Id</th>
                <th scope="col">Title</th>
                <th scope="col" class="number">Year</th>
                <th scope="col">Genres</th>
                <th scope="col" class="number">Ratings</th>
                <th scope="col" class="number">Average</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (movie of movies.value().items; track movie.id) {
                <tr>
                  <td class="number muted">{{ movie.id }}</td>
                  <td>
                    <a [routerLink]="['/movies', movie.id]">{{ movie.title }}</a>
                  </td>
                  <td class="number">{{ movie.year ?? '–' }}</td>
                  <td class="muted">{{ movie.genres.join(', ') || '–' }}</td>
                  <td class="number">{{ movie.ratingStats.count | number }}</td>
                  <td class="number">{{ movie.ratingStats.count ? (movie.ratingStats.avg | number: '1.2-2') : '–' }}</td>
                  <td class="actions">
                    <a matIconButton [routerLink]="['/admin/movies', movie.id, 'edit']" [attr.aria-label]="'Edit ' + movie.title">
                      <mat-icon aria-hidden="true">edit</mat-icon>
                    </a>
                    <button matIconButton type="button" [attr.aria-label]="'Delete ' + movie.title" (click)="remove(movie)">
                      <mat-icon aria-hidden="true">delete</mat-icon>
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <mat-paginator
          aria-label="Pages of movies"
          [length]="movies.value().total"
          [pageSize]="pageSize"
          [pageIndex]="page() - 1"
          [hidePageSize]="true"
          [showFirstLastButtons]="true"
          (page)="page.set($event.pageIndex + 1)"
        />
      }
    </div>
  `,
  styles: `
    .heading {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: 12px;
    }
    .search {
      width: min(100%, 420px);
      margin-bottom: 16px;
    }
    .table-scroll {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      padding: 8px 12px;
      text-align: left;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    th {
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
    }
    .number {
      text-align: right;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    .actions {
      white-space: nowrap;
      text-align: right;
    }
    td a:not([matIconButton]) {
      color: var(--mat-sys-on-surface);
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
    mat-paginator {
      background: transparent;
    }
  `,
})
export class AdminMovies {
  private readonly api = inject(Api);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly pageSize = PageSize;
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly page = signal(1);
  private readonly query = signal('');

  protected readonly movies = rxResource({
    params: () => ({ q: this.query() || undefined, page: this.page(), pageSize: PageSize }),
    stream: ({ params }) => this.api.searchMovies(params),
  });

  constructor() {
    this.search.valueChanges
      .pipe(
        debounceTime(300),
        map((text) => text.trim()),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((q) => {
        this.page.set(1);
        this.query.set(q);
      });
  }

  protected remove(movie: MovieSummary): void {
    const data: ConfirmData = {
      title: `Delete “${movie.title}”?`,
      message:
        movie.ratingStats.count > 0
          ? `This also deletes its ${movie.ratingStats.count} ratings and removes it from every watchlist. It cannot be undone.`
          : 'This cannot be undone.',
      confirmLabel: 'Delete',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.deleteMovie(movie.id).subscribe({
          next: () => {
            this.snackBar.open(`Deleted “${movie.title}”`, 'OK');
            this.movies.reload();
          },
          error: (error: unknown) => this.snackBar.open(errorMessage(error), 'OK'),
        });
      });
  }
}

/** Splits "Drama, Sci-Fi" into its parts, dropping blanks and repeats. */
function splitList(text: string): string[] {
  const seen = new Set<string>();
  return text
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && !seen.has(part.toLowerCase()) && Boolean(seen.add(part.toLowerCase())));
}

/** The form for a new movie (/admin/movies/new) or an existing one (/admin/movies/:id/edit). */
@Component({
  selector: 'app-movie-form',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule, LoadState],
  template: `
    <div class="page form-page">
      <h1 class="page-title">{{ id() === undefined ? 'New movie' : 'Edit movie' }}</h1>
      <p class="page-subtitle">
        {{ id() === undefined ? 'It appears in the catalog as soon as it is saved.' : 'Ratings are kept when a movie is edited.' }}
      </p>

      <app-load-state [loading]="existing.isLoading()" [error]="existing.error()" (retry)="existing.reload()" />

      @if (!existing.isLoading() && !existing.error()) {
        <form class="form-column" [formGroup]="form" (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>Title</mat-label>
            <input matInput formControlName="title" maxlength="300" />
            @if (form.controls.title.invalid) {
              <mat-error>Enter a title.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Year</mat-label>
            <input matInput type="number" formControlName="year" min="1870" max="2100" />
            <mat-hint>Optional</mat-hint>
            @if (form.controls.year.invalid) {
              <mat-error>Enter a year from 1870 to 2100, or leave it empty.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Genres</mat-label>
            <input matInput formControlName="genres" placeholder="Drama, Sci-Fi" />
            <mat-hint>Separate with commas. At least one, at most ten.</mat-hint>
            @if (form.controls.genres.invalid) {
              <mat-error>Enter one to ten genres, separated by commas.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Description</mat-label>
            <textarea matInput formControlName="description" rows="5" maxlength="5000"></textarea>
            @if (form.controls.description.invalid) {
              <mat-error>Enter a description.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Tags</mat-label>
            <input matInput formControlName="tags" placeholder="space, time travel" />
            <mat-hint>Optional. Separate with commas. Tags are searchable.</mat-hint>
          </mat-form-field>

          <mat-form-field>
            <mat-label>Poster image address</mat-label>
            <input matInput type="url" formControlName="posterUrl" placeholder="https://…" />
            <mat-hint>Optional</mat-hint>
            @if (form.controls.posterUrl.invalid) {
              <mat-error>Enter a full address starting with http:// or https://.</mat-error>
            }
          </mat-form-field>

          @if (failure()) {
            <p class="form-error" role="alert">{{ failure() }}</p>
          }

          <div class="buttons">
            <button matButton="filled" type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save movie' }}</button>
            <a matButton routerLink="/admin/movies">Cancel</a>
          </div>
        </form>
      }
    </div>
  `,
  styles: `
    .form-page {
      max-width: 720px;
    }
    mat-form-field {
      margin-bottom: 10px;
    }
    .buttons {
      display: flex;
      gap: 8px;
    }
  `,
})
export class MovieForm {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** The :id part of the address; missing for a new movie. */
  readonly id = input(undefined, { transform: (value: unknown) => (value == null ? undefined : numberAttribute(value)) });

  protected readonly saving = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    year: [null as number | null, [Validators.min(1870), Validators.max(2100)]],
    genres: ['', [Validators.required, (control) => (isGenreListValid(String(control.value)) ? null : { genres: true })]],
    description: ['', [Validators.required, Validators.maxLength(5000)]],
    tags: [''],
    posterUrl: ['', [Validators.pattern(/^https?:\/\/\S+$/), Validators.maxLength(500)]],
  });

  /** The movie being edited. A new movie has nothing to load. */
  protected readonly existing = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.movie(params),
  });

  /** Kept from the loaded movie, because the form does not show it. */
  private readonly tmdbId = computed(() => (this.existing.hasValue() ? (this.existing.value()?.movie.tmdbId ?? null) : null));

  constructor() {
    effect(() => {
      if (!this.existing.hasValue()) return;
      const movie = this.existing.value()?.movie;
      if (!movie) return;
      this.form.reset({
        title: movie.title,
        year: movie.year,
        genres: movie.genres.join(', '),
        description: movie.description,
        tags: movie.tags.join(', '),
        posterUrl: movie.posterUrl ?? '',
      });
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const movie: MovieInput = {
      title: value.title.trim(),
      year: value.year === null || String(value.year) === '' ? null : Number(value.year),
      genres: splitList(value.genres),
      description: value.description.trim(),
      tags: splitList(value.tags),
      posterUrl: value.posterUrl.trim() || null,
      tmdbId: this.tmdbId(),
    };

    this.saving.set(true);
    this.failure.set(null);
    const id = this.id();
    const request = id === undefined ? this.api.createMovie(movie) : this.api.updateMovie(id, movie);
    request.subscribe({
      next: (saved) => {
        this.snackBar.open(id === undefined ? 'Movie added' : 'Movie saved', 'OK');
        void this.router.navigate(['/movies', saved.id]);
      },
      error: (error: unknown) => {
        this.failure.set(errorMessage(error));
        this.saving.set(false);
      },
    });
  }
}

function isGenreListValid(text: string): boolean {
  const genres = splitList(text);
  return genres.length >= 1 && genres.length <= 10;
}
