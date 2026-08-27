import { Component, inject, signal, viewChild } from '@angular/core';
import { AbstractControl, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { FormBuilder } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { TurnstileWidget } from '../../../shared/turnstile/turnstile-widget';

export const passwordMatchValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const password = control.get('password');
  const confirmPassword = control.get('confirmPassword');

  if (!password || !confirmPassword) {
    return null;
  }

  return password.value === confirmPassword.value ? null : { passwordMismatch: true };
};

@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink, TurnstileWidget],
  templateUrl: './register-page.html',
})
export class RegisterPage {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly turnstileWidget = viewChild(TurnstileWidget);

  protected readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]],
  }, { validators: [passwordMatchValidator] });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly turnstileToken = signal<string | null>(null);
  protected readonly turnstileTouched = signal(false);

  protected onTurnstileVerified(token: string): void {
    this.turnstileToken.set(token);
  }

  protected onTurnstileExpiredOrFailed(): void {
    this.turnstileToken.set(null);
  }

  protected submit(): void {
    this.turnstileTouched.set(true);

    const token = this.turnstileToken();
    if (this.form.invalid || !token) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { email, fullName, phone, password } = this.form.getRawValue();

    // Send only registration parameters required by backend api
    this.authService
      .register({ fullName, email, phone, password, turnstileToken: token })
      .pipe(switchMap(() => this.authService.requestVerificationCode(email)))
      .subscribe({
        next: () => this.router.navigate(['/verify-email'], { queryParams: { email } }),
        error: (err: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Registration failed. Please try again.');
          // Turnstile tokens are single-use -- a failed submit (including a rejected token) needs
          // a fresh one before the user can retry.
          this.turnstileToken.set(null);
          this.turnstileWidget()?.reset();
        },
      });
  }
}
