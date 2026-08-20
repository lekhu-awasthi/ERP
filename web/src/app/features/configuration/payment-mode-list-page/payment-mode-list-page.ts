import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PaymentMode } from '../../../core/configuration/configuration.models';

/** Roadmap Phase 2 exit criteria: Admin can create/edit/delete a PaymentMode through this screen. */
@Component({
  selector: 'app-payment-mode-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './payment-mode-list-page.html',
})
export class PaymentModeListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PaymentMode[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
    requiresChequeDetails: [false],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', isActive: true, requiresChequeDetails: false });
  }

  protected startEdit(item: PaymentMode): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, isActive: item.isActive, requiresChequeDetails: item.requiresChequeDetails });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, isActive, requiresChequeDetails } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updatePaymentMode(this.organizationId, editingId, { name, isActive, requiresChequeDetails })
      : this.configurationService.createPaymentMode(this.organizationId, { name, requiresChequeDetails });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save payment mode. Please try again.');
      },
    });
  }

  protected requestDelete(item: PaymentMode): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: PaymentMode): void {
    this.configurationService.deletePaymentMode(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete payment mode. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listPaymentModes(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load payment modes.');
      },
    });
  }
}
