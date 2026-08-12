import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { TdsType } from '../../../core/configuration/configuration.models';

/** Phase 6 -- Admin can create/edit/delete a TdsType through this screen, same shape as
 * CreditTermListPage. */
@Component({
  selector: 'app-tds-type-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './tds-type-list-page.html',
})
export class TdsTypeListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<TdsType[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    ratePct: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ code: '', name: '', ratePct: 0, isActive: true });
  }

  protected startEdit(item: TdsType): void {
    this.editingId.set(item.id);
    this.form.reset({ code: item.code, name: item.name, ratePct: item.ratePct, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { code, name, ratePct, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updateTdsType(this.organizationId, editingId, { code, name, ratePct, isActive })
      : this.configurationService.createTdsType(this.organizationId, { code, name, ratePct });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save TDS type. Please try again.');
      },
    });
  }

  protected requestDelete(item: TdsType): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: TdsType): void {
    this.configurationService.deleteTdsType(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete TDS type. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listTdsTypes(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load TDS types.');
      },
    });
  }
}
