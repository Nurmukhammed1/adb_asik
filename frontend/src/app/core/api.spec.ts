import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Api, errorMessage } from './api';

describe('Api', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { api: TestBed.inject(Api), backend: TestBed.inject(HttpTestingController) };
  }

  it('sends only the filters that are set', () => {
    const { api, backend } = setup();

    api.searchMovies({ q: 'star', genre: undefined, yearFrom: 1990, minRatings: undefined, sort: 'rating', page: 2, pageSize: 24 }).subscribe();

    const request = backend.expectOne((r) => r.url === '/api/movies').request;
    expect(request.params.keys().sort()).toEqual(['page', 'pageSize', 'q', 'sort', 'yearFrom']);
    expect(request.params.get('q')).toBe('star');
    expect(request.params.get('yearFrom')).toBe('1990');
  });

  it('asks for popular movies with or without a genre', () => {
    const { api, backend } = setup();

    api.popular(null, 12).subscribe();
    api.popular('Sci-Fi', 12).subscribe();

    const [all, genre] = backend.match((r) => r.url === '/api/movies/popular');
    expect(all.request.params.has('genre')).toBe(false);
    expect(genre.request.params.get('genre')).toBe('Sci-Fi');
  });

  it('rates with PUT and removes a rating with DELETE on the same address', () => {
    const { api, backend } = setup();

    api.rate(42, 4.5).subscribe();
    api.removeRating(42).subscribe();

    const [put, remove] = backend.match('/api/movies/42/rating');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ rating: 4.5 });
    expect(remove.request.method).toBe('DELETE');
  });
});

describe('errorMessage', () => {
  const response = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

  it('explains that the server cannot be reached', () => {
    expect(errorMessage(response(0))).toContain('Cannot reach the server');
  });

  it("uses the API's own sentence when there is one", () => {
    expect(errorMessage(response(409, { title: 'This username is already taken.' }))).toBe('This username is already taken.');
  });

  it('lists every validation message of a 400 response', () => {
    const problem = { title: 'One or more validation errors occurred.', errors: { Title: ['Enter a title.'], Year: ['Too early.'] } };

    expect(errorMessage(response(400, problem))).toBe('Enter a title. Too early.');
  });

  it('falls back to a general sentence', () => {
    expect(errorMessage(response(404))).toBe('Not found.');
    expect(errorMessage(response(403))).toBe('You are not allowed to do this.');
    expect(errorMessage(response(500))).toBe('The request failed (500).');
    expect(errorMessage(new Error('boom'))).toBe('Something went wrong.');
  });
});
