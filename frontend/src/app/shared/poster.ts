import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** A movie poster, or a colored stand-in with the title while the movie has no poster image. */
@Component({
  selector: 'app-poster',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (movie().posterUrl; as url) {
      <img [src]="url" [alt]="movie().title + ' poster'" loading="lazy" />
    } @else {
      <div class="placeholder" [style.background]="background()">
        <mat-icon aria-hidden="true">movie</mat-icon>
        <span>{{ movie().title }}</span>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      aspect-ratio: 2 / 3;
      border-radius: 12px;
      overflow: hidden;
      background: var(--mat-sys-surface-container-high);
    }
    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }
    .placeholder {
      height: 100%;
      box-sizing: border-box;
      padding: 12px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 8px;
      text-align: center;
      color: rgb(255 255 255 / 92%);
      font: var(--mat-sys-title-small);
    }
    .placeholder span {
      display: -webkit-box;
      -webkit-line-clamp: 4;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }
    mat-icon {
      opacity: 0.7;
    }
  `,
})
export class Poster {
  readonly movie = input.required<{ id: number; title: string; posterUrl: string | null }>();

  /** Every movie keeps its own color, picked from its id. */
  protected readonly background = computed(() => {
    const hue = (this.movie().id * 47) % 360;
    return `linear-gradient(155deg, hsl(${hue} 45% 34%), hsl(${(hue + 40) % 360} 50% 18%))`;
  });
}
