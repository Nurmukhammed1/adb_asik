import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthStore, adminGuard, authGuard, authInterceptor } from './auth';
import { AuthResponse, User } from './models';

const StorageKey = 'cinematch.session';

function user(role: User['role'] = 'user'): User {
  return {
    id: 7,
    username: 'tester',
    email: 'tester@example.com',
    role,
    profile: { displayName: 'Tester', favoriteGenres: [] },
    createdAt: '2026-01-01T00:00:00Z',
  };
}

function session(minutesFromNow: number, role: User['role'] = 'user'): AuthResponse {
  return { token: 'token-123', expiresAt: new Date(Date.now() + minutesFromNow * 60_000).toISOString(), user: user(role) };
}

function setup(stored?: AuthResponse) {
  localStorage.clear();
  if (stored) localStorage.setItem(StorageKey, JSON.stringify(stored));
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  return {
    auth: TestBed.inject(AuthStore),
    http: TestBed.inject(HttpClient),
    backend: TestBed.inject(HttpTestingController),
    router: TestBed.inject(Router),
  };
}

describe('AuthStore', () => {
  it('starts logged out when nothing is stored', () => {
    const { auth } = setup();

    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.validToken()).toBeNull();
  });

  it('keeps a started session across a reload', () => {
    const { auth } = setup();

    auth.start(session(60, 'admin'));

    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.isAdmin()).toBe(true);
    expect(auth.validToken()).toBe('token-123');
    expect(JSON.parse(localStorage.getItem(StorageKey)!).user.username).toBe('tester');
  });

  it('restores a stored session that is still valid', () => {
    const { auth } = setup(session(30));

    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.isAdmin()).toBe(false);
  });

  it('ignores a stored session whose token has expired', () => {
    const { auth } = setup(session(-1));

    expect(auth.isLoggedIn()).toBe(false);
  });

  it('ignores stored data that is not a session', () => {
    localStorage.clear();
    localStorage.setItem(StorageKey, '{not json');
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    expect(TestBed.inject(AuthStore).isLoggedIn()).toBe(false);
  });

  it('ends the session when the token expires while the page is open', () => {
    const { auth } = setup();
    auth.start(session(-1));

    expect(auth.validToken()).toBeNull();
    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(StorageKey)).toBeNull();
  });

  it('stores an edited profile and forgets everything on sign-out', () => {
    const { auth } = setup();
    auth.start(session(60));

    auth.updateUser({ ...user(), profile: { displayName: 'Renamed', favoriteGenres: ['Drama'] } });
    expect(auth.user()?.profile.displayName).toBe('Renamed');
    expect(JSON.parse(localStorage.getItem(StorageKey)!).user.profile.favoriteGenres).toEqual(['Drama']);

    auth.clear();
    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(StorageKey)).toBeNull();
  });
});

describe('authInterceptor', () => {
  it('sends the token with API requests', () => {
    const { http, backend } = setup(session(60));

    http.get('/api/users/me').subscribe();

    expect(backend.expectOne('/api/users/me').request.headers.get('Authorization')).toBe('Bearer token-123');
  });

  it('sends no token when logged out and never to other addresses', () => {
    const loggedOut = setup();
    loggedOut.http.get('/api/movies').subscribe();
    expect(loggedOut.backend.expectOne('/api/movies').request.headers.has('Authorization')).toBe(false);

    TestBed.resetTestingModule();
    const loggedIn = setup(session(60));
    loggedIn.http.get('https://image.tmdb.org/poster.jpg').subscribe();
    expect(loggedIn.backend.expectOne('https://image.tmdb.org/poster.jpg').request.headers.has('Authorization')).toBe(false);
  });

  it('ends the session and asks for a new login when the API rejects the token', () => {
    const { auth, http, backend, router } = setup(session(60));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let failed = false;

    http.get('/api/users/me').subscribe({ error: () => (failed = true) });
    backend.expectOne('/api/users/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(failed).toBe(true);
    expect(auth.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: router.url, expired: 1 } });
  });

  it('treats a 401 without a token as a plain error (a wrong password on the login form)', () => {
    const { http, backend, router } = setup();
    const navigate = vi.spyOn(router, 'navigate');
    let status = 0;

    http.post('/api/auth/login', {}).subscribe({ error: (error) => (status = error.status) });
    backend.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(status).toBe(401);
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe('route guards', () => {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/for-you' } as RouterStateSnapshot;
  const run = (guard: typeof authGuard) => TestBed.runInInjectionContext(() => guard(route, state));

  it('sends a guest to the login page and remembers where they were going', () => {
    const { router } = setup();

    const result = run(authGuard) as UrlTree;

    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Ffor-you');
  });

  it('lets a logged-in user through', () => {
    setup(session(60));

    expect(run(authGuard)).toBe(true);
  });

  it('keeps users who are not admins out of the admin pages', () => {
    const { router } = setup(session(60, 'user'));

    expect(router.serializeUrl(run(adminGuard) as UrlTree)).toBe('/');
  });

  it('lets an admin into the admin pages and sends a guest to the login page', () => {
    setup(session(60, 'admin'));
    expect(run(adminGuard)).toBe(true);

    TestBed.resetTestingModule();
    const { router } = setup();
    expect(router.serializeUrl(run(adminGuard) as UrlTree)).toBe('/login?returnUrl=%2Ffor-you');
  });
});
