import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-verify-email-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './verify-email-page.html',
})
export class VerifyEmailPage {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = this.fb.nonNullable.group({
    email: [this.route.snapshot.queryParamMap.get('email') ?? '', [Validators.required, Validators.email]],
    code: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(6)]],
  });

  protected readonly submitting = signal(false);
  protected readonly resending = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly infoMessage = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.infoMessage.set(null);

    const { email, code } = this.form.getRawValue();

    this.authService.verifyEmail(email, code).subscribe({
      next: () => this.router.navigate(['/login'], { queryParams: { email } }),
      error: (err: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Verification failed. Please try again.');
      },
    });
  }

  protected resendCode(): void {
    const email = this.form.controls.email.value;

    if (!email) {
      this.form.controls.email.markAsTouched();
      return;
    }

    this.resending.set(true);
    this.errorMessage.set(null);
    this.infoMessage.set(null);

    this.authService.requestVerificationCode(email).subscribe({
      next: () => {
        this.resending.set(false);
        this.infoMessage.set('A new code has been sent to your email.');
      },
      error: (err: unknown) => {
        this.resending.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not resend the code. Please try again.');
      },
    });
  }
}
