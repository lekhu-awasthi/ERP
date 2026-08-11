import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { UnitOfMeasurement } from '../../../core/catalog/catalog.models';

@Component({
  selector: 'app-unit-of-measurement-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './unit-of-measurement-list-page.html',
})
export class UnitOfMeasurementListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalogService = inject(CatalogService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<UnitOfMeasurement[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    shortName: ['', [Validators.required, Validators.maxLength(20)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', shortName: '', isActive: true });
  }

  protected startEdit(item: UnitOfMeasurement): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, shortName: item.shortName, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, shortName, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.catalogService.updateUnitOfMeasurement(this.organizationId, editingId, { name, shortName, isActive })
      : this.catalogService.createUnitOfMeasurement(this.organizationId, { name, shortName });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save unit. Please try again.');
      },
    });
  }

  protected requestDelete(item: UnitOfMeasurement): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: UnitOfMeasurement): void {
    this.catalogService.deleteUnitOfMeasurement(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete unit. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.catalogService.listUnitsOfMeasurement(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load units of measurement.');
      },
    });
  }
}
