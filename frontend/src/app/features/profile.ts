import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, linkedSignal, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { AuthStore } from '../core/auth';
import { LoadState } from '../shared/load-state';
import { Poster } from '../shared/poster';
import { StarRating } from '../shared/star-rating';

const PageSize = 10;
const MaxFavoriteGenres = 10;
const Tabs = ['profile', 'ratings', 'views', 'watchlist'];

/** The visitor's account: name and favorite genres, rating and view history, and the watchlist. */
@Component({
  selector: 'app-profile',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    MatTabsModule,
    MatFormFieldModule,
    MatInputModule,
    MatChipsModule,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    LoadState,
    Poster,
    StarRating,
  ],
  template: `
    <div class="page">
      @if (auth.user(); as user) {
        <h1 class="page-title">{{ user.profile.displayName || user.username }}</h1>
        <p class="page-subtitle">
          &#64;{{ user.username }} · {{ user.email }} · member since {{ user.createdAt | date: 'mediumDate' }}
        </p>
      }

      <mat-tab-group [selectedIndex]="tabIndex()" (selectedIndexChange)="tabIndex.set($event)" dynamicHeight>
        <mat-tab label="Profile">
          <form class="form-column profile-form" [formGroup]="form" (ngSubmit)="save()">
            <mat-form-field>
              <mat-label>Display name</mat-label>
              <input matInput formControlName="displayName" maxlength="60" />
              @if (form.controls.displayName.invalid) {
                <mat-error>Enter a name of up to 60 characters.</mat-error>
              }
            </mat-form-field>

            <div>
              <div class="label">Favorite genres</div>
              <p class="muted hint">
                Used for your recommendations until you have rated a few movies. Choose up to {{ maxGenres }}.
              </p>
              @if (genres.hasValue()) {
                <mat-chip-listbox multiple aria-label="Favorite genres" formControlName="favoriteGenres">
                  @for (genre of genres.value(); track genre) {
                    <mat-chip-option [value]="genre">{{ genre }}</mat-chip-option>
                  }
                </mat-chip-listbox>
              }
              @if (form.controls.favoriteGenres.invalid) {
                <p class="form-error">Choose at most {{ maxGenres }} genres.</p>
              }
            </div>

            <div>
              <button matButton="filled" type="submit" [disabled]="saving() || form.pristine">
                {{ saving() ? 'Saving…' : 'Save changes' }}
              </button>
            </div>
          </form>
        </mat-tab>

        <mat-tab label="Ratings">
          <ng-template matTabContent>
            <app-load-state
              [loading]="ratings.isLoading()"
              [error]="ratings.error()"
              [empty]="ratings.hasValue() && ratings.value().total === 0"
              emptyText="You have not rated any movies yet."
              emptyIcon="star"
              (retry)="ratings.reload()"
            />
            @if (ratings.hasValue() && !ratings.isLoading() && ratings.value().total > 0) {
              <ul class="rows">
                @for (item of ratings.value().items; track item.movie.id) {
                  <li>
                    <a class="thumb" [routerLink]="['/movies', item.movie.id]" tabindex="-1" aria-hidden="true">
                      <app-poster [movie]="item.movie" />
                    </a>
                    <div class="row-main">
                      <a [routerLink]="['/movies', item.movie.id]">{{ item.movie.title }}</a>
                      <span class="muted">{{ item.movie.year }} · rated {{ item.ts | date: 'mediumDate' }}</span>
                    </div>
                    <div class="row-side">
                      <app-star-rating [value]="item.rating" [readonly]="true" />
                      <span class="muted">{{ item.rating }}</span>
                    </div>
                  </li>
                }
              </ul>
              <mat-paginator
                aria-label="Pages of ratings"
                [length]="ratings.value().total"
                [pageSize]="pageSize"
                [pageIndex]="ratingsPage() - 1"
                [hidePageSize]="true"
                (page)="ratingsPage.set($event.pageIndex + 1)"
              />
            }
          </ng-template>
        </mat-tab>

        <mat-tab label="Viewed">
          <ng-template matTabContent>
            <app-load-state
              [loading]="views.isLoading()"
              [error]="views.error()"
              [empty]="views.hasValue() && views.value().total === 0"
              emptyText="Movies you open will show up here."
              emptyIcon="visibility"
              (retry)="views.reload()"
            />
            @if (views.hasValue() && !views.isLoading() && views.value().total > 0) {
              <ul class="rows">
                @for (item of views.value().items; track $index) {
                  <li>
                    <a class="thumb" [routerLink]="['/movies', item.movie.id]" tabindex="-1" aria-hidden="true">
                      <app-poster [movie]="item.movie" />
                    </a>
                    <div class="row-main">
                      <a [routerLink]="['/movies', item.movie.id]">{{ item.movie.title }}</a>
                      <span class="muted">{{ item.movie.year }} · viewed {{ item.ts | date: 'medium' }}</span>
                    </div>
                  </li>
                }
              </ul>
              <mat-paginator
                aria-label="Pages of viewed movies"
                [length]="views.value().total"
                [pageSize]="pageSize"
                [pageIndex]="viewsPage() - 1"
                [hidePageSize]="true"
                (page)="viewsPage.set($event.pageIndex + 1)"
              />
            }
          </ng-template>
        </mat-tab>

        <mat-tab label="Watchlist">
          <ng-template matTabContent>
            <app-load-state
              [loading]="watchlist.isLoading()"
              [error]="watchlist.error()"
              [empty]="watchlist.hasValue() && watchlist.value().total === 0"
              emptyText="Your watchlist is empty. Add movies from their pages."
              emptyIcon="bookmark"
              (retry)="watchlist.reload()"
            />
            @if (watchlist.hasValue() && !watchlist.isLoading() && watchlist.value().total > 0) {
              <ul class="rows">
                @for (entry of watchlist.value().items; track entry.movie.id) {
                  <li>
                    <a class="thumb" [routerLink]="['/movies', entry.movie.id]" tabindex="-1" aria-hidden="true">
                      <app-poster [movie]="entry.movie" />
                    </a>
                    <div class="row-main">
                      <a [routerLink]="['/movies', entry.movie.id]">{{ entry.movie.title }}</a>
                      <span class="muted">{{ entry.movie.year }} · added {{ entry.addedAt | date: 'mediumDate' }}</span>
                    </div>
                    <div class="row-side">
                      <button
                        matIconButton
                        type="button"
                        [attr.aria-label]="'Remove ' + entry.movie.title + ' from watchlist'"
                        (click)="removeFromWatchlist(entry.movie.id)"
                      >
                        <mat-icon aria-hidden="true">bookmark_remove</mat-icon>
                      </button>
                    </div>
                  </li>
                }
              </ul>
              <mat-paginator
                aria-label="Pages of the watchlist"
                [length]="watchlist.value().total"
                [pageSize]="pageSize"
                [pageIndex]="watchlistPage() - 1"
                [hidePageSize]="true"
                (page)="watchlistPage.set($event.pageIndex + 1)"
              />
            }
          </ng-template>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `
    .profile-form {
      max-width: 640px;
      gap: 16px;
      padding-top: 24px;
    }
    .label {
      font: var(--mat-sys-title-medium);
    }
    .hint {
      margin: 4px 0 12px;
    }
    .rows {
      list-style: none;
      margin: 16px 0 0;
      padding: 0;
    }
    .rows li {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 10px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .thumb {
      flex: none;
      width: 44px;
    }
    .thumb app-poster {
      border-radius: 6px;
      font-size: 0; // the stand-in poster is too small for its title
    }
    .row-main {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .row-main a {
      font: var(--mat-sys-title-small);
      color: var(--mat-sys-on-surface);
      text-decoration: none;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .row-main a:hover {
      text-decoration: underline;
    }
    .row-side {
      display: flex;
      align-items: center;
      gap: 8px;
      --star-size: 18px;
    }
    mat-paginator {
      background: transparent;
    }
  `,
})
export class Profile {
  private readonly api = inject(Api);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly auth = inject(AuthStore);

  /** ?tab=watchlist opens that tab directly. */
  readonly tab = input<string>();

  protected readonly pageSize = PageSize;
  protected readonly maxGenres = MaxFavoriteGenres;
  protected readonly tabIndex = linkedSignal(() => Math.max(0, Tabs.indexOf(this.tab() ?? 'profile')));
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(60)]],
    favoriteGenres: [[] as string[], Validators.maxLength(MaxFavoriteGenres)],
  });

  protected readonly genres = rxResource({ stream: () => this.api.genres() });

  protected readonly ratingsPage = signal(1);
  protected readonly ratings = rxResource({
    params: () => this.ratingsPage(),
    stream: ({ params }) => this.api.history('ratings', params, PageSize),
  });

  protected readonly viewsPage = signal(1);
  protected readonly views = rxResource({
    params: () => this.viewsPage(),
    stream: ({ params }) => this.api.history('views', params, PageSize),
  });

  protected readonly watchlistPage = signal(1);
  protected readonly watchlist = rxResource({
    params: () => this.watchlistPage(),
    stream: ({ params }) => this.api.watchlist(params, PageSize),
  });

  private readonly profile = computed(() => this.auth.user()?.profile ?? null);

  constructor() {
    // Fill the form from the stored profile, and again whenever a save changes it.
    effect(() => {
      const profile = this.profile();
      if (profile) {
        this.form.reset({ displayName: profile.displayName, favoriteGenres: [...profile.favoriteGenres] });
      }
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const { displayName, favoriteGenres } = this.form.getRawValue();
    this.api.updateProfile({ displayName: displayName.trim(), favoriteGenres }).subscribe({
      next: (user) => {
        this.auth.updateUser(user);
        this.saving.set(false);
        this.snackBar.open('Profile saved', 'OK');
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.snackBar.open(errorMessage(error), 'OK');
      },
    });
  }

  protected removeFromWatchlist(movieId: number): void {
    this.api.removeFromWatchlist(movieId).subscribe({
      next: () => {
        // Removing the last entry of a page steps back one page.
        const shown = this.watchlist.hasValue() ? this.watchlist.value().items.length : 0;
        if (shown === 1 && this.watchlistPage() > 1) this.watchlistPage.update((page) => page - 1);
        else this.watchlist.reload();
        this.snackBar.open('Removed from your watchlist', 'OK');
      },
      error: (error: unknown) => this.snackBar.open(errorMessage(error), 'OK'),
    });
  }
}
