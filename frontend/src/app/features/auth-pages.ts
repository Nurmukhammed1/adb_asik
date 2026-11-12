import { Component, Directive, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, errorMessage } from '../core/api';
import { AuthStore } from '../core/auth';
import { AuthResponse } from '../core/models';

/** Only paths inside this site are accepted as a place to return to after signing in. */
function safeReturnUrl(url: string | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') ? url : '/';
}

/** What the sign-in and sign-up pages share: sending the form and starting the session. */
@Directive()
abstract class AuthPage {
  protected readonly api = inject(Api);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  /** Where the user was heading when they were sent here. */
  readonly returnUrl = input<string>();

  protected readonly busy = signal(false);
  protected readonly failure = signal<unknown>(null);

  protected message(): string {
    return errorMessage(this.failure());
  }

  protected send(request: Observable<AuthResponse>): void {
    this.busy.set(true);
    this.failure.set(null);
    request.subscribe({
      next: (response) => {
        this.auth.start(response);
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (error: unknown) => {
        this.failure.set(error);
        this.busy.set(false);
      },
    });
  }
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <div class="page narrow">
      <div class="surface-card">
        <h1 class="page-title">Sign in</h1>
        <p class="page-subtitle">
          @if (expired()) {
            Your session has ended. Please sign in again.
          } @else {
            Rate movies and get recommendations made for you.
          }
        </p>

        <form class="form-column" [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>Username or email</mat-label>
            <input matInput formControlName="username" autocomplete="username" />
            @if (form.controls.username.hasError('required')) {
              <mat-error>Enter your username or email.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password" />
            @if (form.controls.password.hasError('required')) {
              <mat-error>Enter your password.</mat-error>
            }
          </mat-form-field>

          @if (failure()) {
            <p class="form-error" role="alert">{{ message() }}</p>
          }

          <button matButton="filled" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>

        <p class="muted switch">
          New here?
          <a routerLink="/register" [queryParams]="{ returnUrl: returnUrl() }">Create an account</a>
        </p>
        <p class="muted hint">The imported MovieLens accounts are named user1, user2, user3 and so on.</p>
      </div>
    </div>
  `,
  styles: `
    .switch {
      margin: 20px 0 0;
    }
    .hint {
      margin: 8px 0 0;
      font: var(--mat-sys-body-small);
    }
  `,
})
export class Login extends AuthPage {
  /** Set when an expired or rejected token brought the user here. */
  readonly expired = input<string>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    username: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { username, password } = this.form.getRawValue();
    this.send(this.api.login(username.trim(), password));
  }
}

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <div class="page narrow">
      <div class="surface-card">
        <h1 class="page-title">Create an account</h1>
        <p class="page-subtitle">It takes a minute and needs no confirmation email.</p>

        <form class="form-column" [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>Username</mat-label>
            <input matInput formControlName="username" autocomplete="username" />
            <mat-hint>3–30 letters, digits or underscores</mat-hint>
            @if (form.controls.username.invalid) {
              <mat-error>Use 3–30 letters, digits or underscores.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Email</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="email" />
            @if (form.controls.email.invalid) {
              <mat-error>Enter a valid email address.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="new-password" />
            <mat-hint>At least 8 characters</mat-hint>
            @if (form.controls.password.invalid) {
              <mat-error>Use at least 8 characters.</mat-error>
            }
          </mat-form-field>

          <mat-form-field>
            <mat-label>Display name (optional)</mat-label>
            <input matInput formControlName="displayName" autocomplete="nickname" maxlength="60" />
          </mat-form-field>

          @if (failure()) {
            <p class="form-error" role="alert">{{ message() }}</p>
          }

          <button matButton="filled" type="submit" [disabled]="busy()">
            {{ busy() ? 'Creating account…' : 'Sign up' }}
          </button>
        </form>

        <p class="muted switch">
          Already have an account?
          <a routerLink="/login" [queryParams]="{ returnUrl: returnUrl() }">Sign in</a>
        </p>
      </div>
    </div>
  `,
  styles: `
    mat-form-field {
      margin-bottom: 8px;
    }
    .switch {
      margin: 20px 0 0;
    }
  `,
})
export class Register extends AuthPage {
  protected readonly form = inject(NonNullableFormBuilder).group({
    username: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_]{3,30}$/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
    displayName: ['', Validators.maxLength(60)],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { username, email, password, displayName } = this.form.getRawValue();
    this.send(this.api.register({ username, email, password, displayName: displayName.trim() || undefined }));
  }
}
