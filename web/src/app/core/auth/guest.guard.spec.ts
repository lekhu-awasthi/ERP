import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { guestGuard } from './guest.guard';

describe('guestGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends a signed-in user from /login to /organizations', async () => {
    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
    expect(isObservable(result)).toBe(true);
    const resultPromise = firstValueFrom(result as Observable<boolean | UrlTree>);

    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({ userId: '1', email: 'jane@example.com' });

    const outcome = await resultPromise;
    expect(outcome).toBeInstanceOf(UrlTree);
    expect((outcome as UrlTree).toString()).toBe('/organizations');
  });

  it('shows the sign-in page when nobody is signed in', async () => {
    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
    const resultPromise = firstValueFrom(result as Observable<boolean | UrlTree>);

    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await resultPromise).toBe(true);
  });
});
