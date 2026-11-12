import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from './core/auth';

/** The frame around every page: top bar with navigation, search and account menu, and the footer. */
@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDividerModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  /** The top-bar search opens the catalog with the text as its query. */
  protected search(event: Event, box: HTMLInputElement): void {
    event.preventDefault();
    const q = box.value.trim();
    void this.router.navigate(['/movies'], { queryParams: q ? { q } : {} });
    box.value = '';
    box.blur();
  }

  protected signOut(): void {
    this.auth.clear();
    void this.router.navigate(['/']);
  }
}
