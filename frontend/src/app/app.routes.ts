import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth';

// Every page is loaded on demand, so the first visit downloads only what it shows.
export const routes: Routes = [
  { path: '', title: 'CineMatch', loadComponent: () => import('./features/home').then((m) => m.Home) },
  { path: 'movies', title: 'Browse movies · CineMatch', loadComponent: () => import('./features/catalog').then((m) => m.Catalog) },
  { path: 'movies/:id', title: 'Movie · CineMatch', loadComponent: () => import('./features/movie-page').then((m) => m.MoviePage) },
  {
    path: 'for-you',
    title: 'For You · CineMatch',
    canActivate: [authGuard],
    loadComponent: () => import('./features/for-you').then((m) => m.ForYou),
  },
  {
    path: 'profile',
    title: 'Profile · CineMatch',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile').then((m) => m.Profile),
  },
  { path: 'login', title: 'Sign in · CineMatch', loadComponent: () => import('./features/auth-pages').then((m) => m.Login) },
  { path: 'register', title: 'Sign up · CineMatch', loadComponent: () => import('./features/auth-pages').then((m) => m.Register) },
  {
    path: 'admin',
    canActivate: [adminGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'movies' },
      { path: 'movies', title: 'Manage movies · CineMatch', loadComponent: () => import('./features/admin-movies').then((m) => m.AdminMovies) },
      { path: 'movies/new', title: 'New movie · CineMatch', loadComponent: () => import('./features/admin-movies').then((m) => m.MovieForm) },
      { path: 'movies/:id/edit', title: 'Edit movie · CineMatch', loadComponent: () => import('./features/admin-movies').then((m) => m.MovieForm) },
      {
        path: 'recommender',
        title: 'Recommender · CineMatch',
        loadComponent: () => import('./features/admin-recommender').then((m) => m.AdminRecommender),
      },
    ],
  },
  { path: '**', title: 'Not found · CineMatch', loadComponent: () => import('./features/not-found').then((m) => m.NotFound) },
];
