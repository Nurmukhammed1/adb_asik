import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthResponse, User } from './models';

interface Session {
  token: string;
  expiresAt: string;
  user: User;
}

const StorageKey = 'cinematch.session';

/** Who is logged in. The session survives a page reload and ends when its token expires. */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly session = signal<Session | null>(readStoredSession());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.user.role === 'admin');

  /** The token if it is still valid. An expired session is ended on the spot. */
  validToken(): string | null {
    const session = this.session();
    if (!session) return null;
    if (isExpired(session)) {
      this.clear();
      return null;
    }
    return session.token;
  }

  start(auth: AuthResponse): void {
    this.store({ token: auth.token, expiresAt: auth.expiresAt, user: auth.user });
  }

  /** After the profile was edited. */
  updateUser(user: User): void {
    const session = this.session();
    if (session) this.store({ ...session, user });
  }

  clear(): void {
    this.session.set(null);
    try {
      localStorage.removeItem(StorageKey);
    } catch {
      // Storage can be blocked; the in-memory session is already gone.
    }
  }

  private store(session: Session): void {
    this.session.set(session);
    try {
      localStorage.setItem(StorageKey, JSON.stringify(session));
    } catch {
      // Without storage the session simply lasts until the page is reloaded.
    }
  }
}

function isExpired(session: Session): boolean {
  return new Date(session.expiresAt).getTime() <= Date.now();
}

function readStoredSession(): Session | null {
  try {
    const raw = localStorage.getItem(StorageKey);
    const session = raw ? (JSON.parse(raw) as Session) : null;
    return session?.token && session.user && !isExpired(session) ? session : null;
  } catch {
    return null;
  }
}

/** Sends the login token with API requests. A rejected token ends the session and asks for a new login. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  const token = request.url.startsWith('/api/') ? auth.validToken() : null;
  const outgoing = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      // Only a 401 for a request that carried a token means the session is over;
      // a 401 from the login form itself is just a wrong password.
      if (token && error instanceof HttpErrorResponse && error.status === 401) {
        auth.clear();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url, expired: 1 } });
      }
      return throwError(() => error);
    }),
  );
};

/** Pages for logged-in users: others go to the login page and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  return auth.validToken() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Pages for administrators. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (!auth.validToken()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return auth.isAdmin() ? true : router.createUrlTree(['/']);
};
