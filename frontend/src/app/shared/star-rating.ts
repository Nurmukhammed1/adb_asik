import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Five stars in half-star steps (0.5 to 5), matching the API's rating scale.
 * Each star has two click areas: its left half and its right half.
 */
@Component({
  selector: 'app-star-rating',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stars" [class.readonly]="readonly()" (mouseleave)="hovered.set(null)">
      @for (star of stars; track star) {
        <span class="star">
          <mat-icon [class.filled]="shown() >= star - 0.5" aria-hidden="true">
            {{ shown() >= star - 0.5 && shown() < star ? 'star_half' : 'star' }}
          </mat-icon>
          @if (!readonly()) {
            <button
              type="button"
              class="half left"
              [attr.aria-label]="'Rate ' + (star - 0.5) + ' out of 5'"
              [attr.aria-pressed]="value() === star - 0.5"
              (mouseenter)="hovered.set(star - 0.5)"
              (focus)="hovered.set(star - 0.5)"
              (blur)="hovered.set(null)"
              (click)="rated.emit(star - 0.5)"
            ></button>
            <button
              type="button"
              class="half right"
              [attr.aria-label]="'Rate ' + star + ' out of 5'"
              [attr.aria-pressed]="value() === star"
              (mouseenter)="hovered.set(star)"
              (focus)="hovered.set(star)"
              (blur)="hovered.set(null)"
              (click)="rated.emit(star)"
            ></button>
          }
        </span>
      }
    </div>
  `,
  styles: `
    .stars {
      display: inline-flex;
      gap: 2px;
    }
    .star {
      position: relative;
      display: inline-flex;
    }
    mat-icon {
      font-size: var(--star-size, 32px);
      width: var(--star-size, 32px);
      height: var(--star-size, 32px);
      color: var(--mat-sys-outline);
    }
    mat-icon.filled {
      color: var(--app-star);
    }
    .half {
      position: absolute;
      top: 0;
      bottom: 0;
      width: 50%;
      padding: 0;
      border: 0;
      background: none;
      cursor: pointer;
      border-radius: 4px;
    }
    .half.left {
      left: 0;
    }
    .half.right {
      right: 0;
    }
    .half:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
    }
  `,
})
export class StarRating {
  /** The current rating, or null for "not rated". */
  readonly value = input<number | null>(null);
  readonly readonly = input(false);
  readonly rated = output<number>();

  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly hovered = signal<number | null>(null);

  /** While the pointer is over the stars they preview the rating a click would give. */
  protected readonly shown = computed(() => (this.readonly() ? null : this.hovered()) ?? this.value() ?? 0);
}
