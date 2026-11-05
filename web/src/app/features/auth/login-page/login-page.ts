import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AuthService } from '../../../core/auth/auth.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink, StatusBanner],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { email, password } = this.form.getRawValue();

    // The login response carries the user, so the app looks signed in whether or not the browser
    // kept the session cookie. A private window blocks it (the API is another site in dev, so its
    // cookie is third-party), and every later request was then a 401 shown as "Could not load your
    // organizations". Ask /me once before moving on, so a dropped cookie is named here instead.
    this.authService
      .login(email, password)
      .pipe(switchMap(() => this.authService.fetchCurrentUser()))
      .subscribe({
        next: (user) => {
          if (user) {
            this.router.navigate(['/organizations']);
            return;
          }

          this.submitting.set(false);
          this.errorMessage.set(
            'Your password was accepted, but this browser did not keep the sign-in. ' +
              'Private or incognito windows block the cookie that keeps you signed in. ' +
              'Allow cookies for this site, or use a normal window.',
          );
        },
        error: (err: unknown) => {
          this.submitting.set(false);

          if (err instanceof HttpErrorResponse && err.status === 403) {
            this.router.navigate(['/verify-email'], { queryParams: { email } });
            return;
          }

          this.errorMessage.set(extractErrorMessage(err) ?? 'Login failed. Please try again.');
        },
      });
  }
}
