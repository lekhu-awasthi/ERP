import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { DatePreferenceService } from '../../shared/formatting/date-preference';
import { calendarInterceptor } from './calendar-interceptor';

describe('calendarInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let preference: DatePreferenceService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([calendarInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    preference = TestBed.inject(DatePreferenceService);
    preference.set('AD');
  });

  afterEach(() => {
    preference.set('AD');
    httpMock.verify();
  });

  it('sends the active calendar on API requests', () => {
    http.get(`${environment.apiBaseUrl}/api/anything`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/anything`);
    expect(request.request.headers.get('X-Calendar')).toBe('AD');
    request.flush({});
  });

  it('follows the user flipping the calendar toggle', () => {
    preference.set('BS');
    http.get(`${environment.apiBaseUrl}/api/anything`).subscribe();

    const request = httpMock.expectOne(`${environment.apiBaseUrl}/api/anything`);
    // This header is the whole mechanism behind BS dates in server-rendered PDFs and .xlsx
    // exports -- phase-23 Decision A's carried limitation.
    expect(request.request.headers.get('X-Calendar')).toBe('BS');
    request.flush({});
  });

  it('tells a third-party host nothing about the user', () => {
    // A preference is still information about a person; it belongs only on this app's own API.
    http.get('https://example.com/whatever').subscribe();

    const request = httpMock.expectOne('https://example.com/whatever');
    expect(request.request.headers.has('X-Calendar')).toBe(false);
    request.flush({});
  });
});
