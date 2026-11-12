import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Api, errorMessage } from '../core/api';
import { EvaluationReport } from '../core/models';
import { LoadState } from '../shared/load-state';

/** The engine room: the state of the similarity table, a rebuild button, and the accuracy check. */
@Component({
  selector: 'app-admin-recommender',
  imports: [DatePipe, DecimalPipe, PercentPipe, MatButtonModule, MatIconModule, MatProgressBarModule, LoadState],
  template: `
    <div class="page">
      <h1 class="page-title">Recommender</h1>
      <p class="page-subtitle">
        Recommendations are computed from a table of similar movies. It is rebuilt from all ratings every night;
        new ratings are used at once, but new similarities only appear after a rebuild.
      </p>

      <app-load-state [loading]="status.isLoading()" [error]="status.error()" (retry)="status.reload()" />

      @if (status.hasValue() && !status.isLoading()) {
        @let current = status.value();
        <section class="surface-card">
          <h2>Similarity table</h2>
          <dl class="facts">
            <div>
              <dt>Movies with similar movies</dt>
              <dd>{{ current.moviesWithNeighbors | number }}</dd>
            </div>
            @if (current.lastRebuild; as last) {
              <div>
                <dt>Last rebuild in this session</dt>
                <dd>{{ last.startedAt | date: 'medium' }}</dd>
              </div>
              <div>
                <dt>Ratings read</dt>
                <dd>{{ last.ratings | number }}</dd>
              </div>
              <div>
                <dt>Users · movies</dt>
                <dd>{{ last.users | number }} · {{ last.movies | number }}</dd>
              </div>
              <div>
                <dt>Similar pairs stored</dt>
                <dd>{{ last.storedPairs | number }}</dd>
              </div>
              <div>
                <dt>Time: load · build · save</dt>
                <dd>{{ last.loadSeconds }} s · {{ last.buildSeconds }} s · {{ last.saveSeconds }} s</dd>
              </div>
            } @else {
              <div>
                <dt>Last rebuild in this session</dt>
                <dd>None since the API started</dd>
              </div>
            }
          </dl>

          @if (current.lastError) {
            <p class="form-error" role="alert">The last rebuild failed: {{ current.lastError }}</p>
          }

          <button matButton="filled" type="button" [disabled]="rebuilding() || current.running" (click)="rebuild()">
            <mat-icon aria-hidden="true">refresh</mat-icon>
            {{ rebuilding() || current.running ? 'Rebuilding…' : 'Rebuild now' }}
          </button>
          @if (rebuilding() || current.running) {
            <mat-progress-bar mode="indeterminate" aria-label="Rebuilding the similarity table" />
          }
        </section>

        <section class="surface-card">
          <h2>Settings</h2>
          <dl class="facts">
            <div>
              <dt>Similar movies kept per movie</dt>
              <dd>{{ current.settings.neighborsPerMovie }}</dd>
            </div>
            <div>
              <dt>Minimum users who rated both</dt>
              <dd>{{ current.settings.minCommonRaters }}</dd>
            </div>
            <div>
              <dt>Shrinkage</dt>
              <dd>{{ current.settings.shrinkage }}</dd>
            </div>
            <div>
              <dt>Lists ranked by</dt>
              <dd>{{ current.settings.rankBy === 'evidence' ? 'Summed evidence' : 'Predicted rating' }}</dd>
            </div>
            <div>
              <dt>Ratings needed for personal lists</dt>
              <dd>{{ current.settings.minRatingsForPersonal }}</dd>
            </div>
            <div>
              <dt>Nightly rebuild</dt>
              <dd>{{ current.settings.nightlyRebuildHour === null ? 'Off' : current.settings.nightlyRebuildHour + ':00' }}</dd>
            </div>
          </dl>
          <p class="muted">Settings are changed in the API's appsettings.json.</p>
        </section>
      }

      <section class="surface-card">
        <h2>Accuracy check</h2>
        <p class="muted">
          Hides 20% of every user's ratings, rebuilds from the rest in memory, and measures how well the hidden
          ratings are predicted and how many hidden liked movies the top-10 lists find. The stored table is not
          touched.
        </p>
        <button matButton="tonal" type="button" [disabled]="evaluating()" (click)="evaluate()">
          <mat-icon aria-hidden="true">query_stats</mat-icon>
          {{ evaluating() ? 'Measuring…' : 'Run accuracy check' }}
        </button>
        @if (evaluating()) {
          <mat-progress-bar mode="indeterminate" aria-label="Running the accuracy check" />
        }

        @if (report(); as report) {
          <p class="muted">
            {{ report.users | number }} users, {{ report.trainRatings | number }} ratings to learn from,
            {{ report.testRatings | number }} hidden.
          </p>

          <h3>Rating prediction error (RMSE, lower is better)</h3>
          <table>
            <tbody>
              <tr class="ours">
                <th scope="row">Item-based collaborative filtering</th>
                <td>{{ report.rmseItemBased | number: '1.3-3' }}</td>
              </tr>
              <tr>
                <th scope="row">Baseline: the user's own average</th>
                <td>{{ report.rmseUserAverage | number: '1.3-3' }}</td>
              </tr>
              <tr>
                <th scope="row">Baseline: the global average</th>
                <td>{{ report.rmseGlobalAverage | number: '1.3-3' }}</td>
              </tr>
            </tbody>
          </table>
          <p class="muted">
            A neighbor-based prediction was possible for {{ report.predictionCoverage | percent: '1.0-1' }} of the hidden
            ratings.
          </p>

          <h3>Top-{{ report.listSize }} lists for {{ report.topNUsers | number }} users (higher is better)</h3>
          <table>
            <thead>
              <tr>
                <th scope="col">Method</th>
                <th scope="col">Precision</th>
                <th scope="col">Recall</th>
                <th scope="col">Catalog coverage</th>
              </tr>
            </thead>
            <tbody>
              <tr class="ours">
                <th scope="row">Item-based, ranked by evidence</th>
                <td>{{ report.itemBasedByEvidence.precision | number: '1.3-3' }}</td>
                <td>{{ report.itemBasedByEvidence.recall | number: '1.3-3' }}</td>
                <td>{{ report.itemBasedByEvidence.catalogCoverage | percent: '1.1-1' }}</td>
              </tr>
              <tr>
                <th scope="row">Item-based, ranked by predicted rating</th>
                <td>{{ report.itemBasedByPredictedRating.precision | number: '1.3-3' }}</td>
                <td>{{ report.itemBasedByPredictedRating.recall | number: '1.3-3' }}</td>
                <td>{{ report.itemBasedByPredictedRating.catalogCoverage | percent: '1.1-1' }}</td>
              </tr>
              <tr>
                <th scope="row">Baseline: most-rated movies</th>
                <td>{{ report.mostPopular.precision | number: '1.3-3' }}</td>
                <td>{{ report.mostPopular.recall | number: '1.3-3' }}</td>
                <td>{{ report.mostPopular.catalogCoverage | percent: '1.1-1' }}</td>
              </tr>
            </tbody>
          </table>
        }
      </section>
    </div>
  `,
  styles: `
    section {
      margin-bottom: 20px;
    }
    h2 {
      font: var(--mat-sys-title-large);
      margin: 0 0 12px;
    }
    h3 {
      font: var(--mat-sys-title-medium);
      margin: 24px 0 8px;
    }
    .facts {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(230px, 1fr));
      gap: 16px;
      margin: 0 0 20px;
    }
    dt {
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 2px 0 0;
      font: var(--mat-sys-title-medium);
    }
    mat-progress-bar {
      margin-top: 12px;
    }
    table {
      border-collapse: collapse;
      width: 100%;
      max-width: 720px;
    }
    th,
    td {
      padding: 8px 12px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      text-align: left;
      font-weight: 400;
    }
    td,
    thead th:not(:first-child) {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    thead th {
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
    }
    .ours th,
    .ours td {
      font-weight: 500;
      color: var(--mat-sys-primary);
    }
  `,
})
export class AdminRecommender {
  private readonly api = inject(Api);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly status = rxResource({ stream: () => this.api.recommenderStatus() });
  protected readonly rebuilding = signal(false);
  protected readonly evaluating = signal(false);
  protected readonly report = signal<EvaluationReport | null>(null);

  protected rebuild(): void {
    this.rebuilding.set(true);
    this.api.rebuildSimilarities().subscribe({
      next: (result) => {
        this.rebuilding.set(false);
        this.status.reload();
        this.snackBar.open(`Rebuilt: ${result.moviesWithNeighbors.toLocaleString('en')} movies have similar movies`, 'OK');
      },
      error: (error: unknown) => {
        this.rebuilding.set(false);
        this.status.reload();
        this.snackBar.open(errorMessage(error), 'OK');
      },
    });
  }

  protected evaluate(): void {
    this.evaluating.set(true);
    this.api.evaluateRecommender().subscribe({
      next: (report) => {
        this.report.set(report);
        this.evaluating.set(false);
      },
      error: (error: unknown) => {
        this.evaluating.set(false);
        this.snackBar.open(errorMessage(error), 'OK');
      },
    });
  }
}
