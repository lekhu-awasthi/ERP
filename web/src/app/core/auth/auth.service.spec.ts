import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('login posts credentials and stores the current user', () => {
    expect(service.currentUser()).toBeNull();

    service.login('jane@example.com', 'Password123').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush({ userId: '1', email: 'jane@example.com', fullName: 'Jane Doe' });

    expect(service.currentUser()).toEqual({ userId: '1', email: 'jane@example.com', fullName: 'Jane Doe' });
  });

  it('fetchCurrentUser clears the current user on a failed /me call', () => {
    service.fetchCurrentUser().subscribe((user) => expect(user).toBeNull());

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`);
    req.flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(service.currentUser()).toBeNull();
  });

  it('register posts to the register endpoint', () => {
    service
      .register({
        fullName: 'Jane Doe',
        email: 'jane@example.com',
        phone: '9800000000',
        password: 'Password123',
        turnstileToken: 'turnstile-token',
      })
      .subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/register`);
    expect(req.request.method).toBe('POST');
    req.flush({ userId: '1', email: 'jane@example.com' });
  });
});
