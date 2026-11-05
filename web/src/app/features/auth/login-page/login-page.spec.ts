import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { environment } from '../../../../environments/environment';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => httpMock.verify());

  function signIn(): HTMLElement {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const email = host.querySelector<HTMLInputElement>('input[type="email"]')!;
    const password = host.querySelector<HTMLInputElement>('input[type="password"]')!;
    email.value = 'jane@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'secret';
    password.dispatchEvent(new Event('input'));
    host.querySelector('form')!.dispatchEvent(new Event('submit'));
    httpMock
      .expectOne(`${environment.apiBaseUrl}/api/auth/login`)
      .flush({ userId: '1', email: 'jane@example.com', fullName: 'Jane' });
    return host;
  }

  it('goes to the organizations once the session is confirmed', () => {
    signIn();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush({ userId: '1', email: 'jane@example.com' });

    expect(navigate).toHaveBeenCalledWith(['/organizations']);
  });

  it('says the browser dropped the sign-in when the session does not stick', () => {
    const host = signIn();
    httpMock.expectOne(`${environment.apiBaseUrl}/api/auth/me`).flush(null, { status: 401, statusText: 'Unauthorized' });
    TestBed.tick();

    expect(navigate).not.toHaveBeenCalled();
    expect(host.textContent).toContain('this browser did not keep the sign-in');
  });
});
