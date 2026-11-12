import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule],
  template: `
    <div class="page narrow center">
      <h1 class="page-title">Page not found</h1>
      <p class="page-subtitle">There is nothing at this address.</p>
      <a matButton="filled" routerLink="/movies">Browse movies</a>
    </div>
  `,
  styles: `
    .center {
      text-align: center;
      padding-top: 72px;
    }
  `,
})
export class NotFound {}
