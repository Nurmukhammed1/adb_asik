import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { errorMessage } from '../core/api';

/**
 * What to show instead of content: a spinner while loading, the error with a retry button,
 * or a line for an empty result. Shows nothing when there is content.
 */
@Component({
  selector: 'app-load-state',
  imports: [MatProgressSpinnerModule, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <div class="state" role="status" aria-label="Loading">
        <mat-spinner diameter="36" />
      </div>
    } @else if (error()) {
      <div class="state" role="alert">
        <mat-icon aria-hidden="true">error</mat-icon>
        <p>{{ message() }}</p>
        <button matButton="outlined" type="button" (click)="retry.emit()">Try again</button>
      </div>
    } @else if (empty()) {
      <div class="state">
        <mat-icon aria-hidden="true">{{ emptyIcon() }}</mat-icon>
        <p>{{ emptyText() }}</p>
      </div>
    }
  `,
  styles: `
    .state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 48px 16px;
      color: var(--mat-sys-on-surface-variant);
      text-align: center;
    }
    p {
      margin: 0;
      max-width: 48ch;
    }
    mat-icon {
      font-size: 40px;
      width: 40px;
      height: 40px;
    }
  `,
})
export class LoadState {
  readonly loading = input(false);
  readonly error = input<unknown>(null);
  readonly empty = input(false);
  readonly emptyText = input('Nothing here yet.');
  readonly emptyIcon = input('inbox');
  readonly retry = output<void>();

  protected readonly message = computed(() => errorMessage(this.error()));
}

export interface ConfirmData {
  title: string;
  message: string;
  confirmLabel: string;
}

/** A yes/no question before something that cannot be undone. Closes with true on confirm. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" [mat-dialog-close]="false">Cancel</button>
      <button matButton="filled" type="button" [mat-dialog-close]="true" cdkFocusInitial>{{ data.confirmLabel }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}
