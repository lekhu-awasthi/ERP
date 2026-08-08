import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('redirects to /login when /me fails', async () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(isObservable(result)).toBe(true);

    const resultPromise = firstValueFrom(result as Observable<boolean | UrlTree>);

    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush(null, { status: 401, statusText: 'Unauthorized' });

    const outcome = await resultPromise;
    expect(outcome).toBeInstanceOf(UrlTree);
    expect((outcome as UrlTree).toString()).toBe('/login');
  });

  it('allows navigation when /me succeeds', async () => {
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    const resultPromise = firstValueFrom(result as Observable<boolean | UrlTree>);

    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({ userId: '1', email: 'jane@example.com' });

    expect(await resultPromise).toBe(true);
  });
});
