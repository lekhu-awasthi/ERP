import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';

/** Minimal single-column lookup screen, same clone-of-unit-of-measurement-list-page shape --
 * see Domain.Tenancy.Warehouse's doc comment for why this stays deliberately thin this phase. */
@Component({
  selector: 'app-warehouse-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './warehouse-list-page.html',
})
export class WarehouseListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Warehouse[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', isActive: true });
  }

  protected startEdit(item: Warehouse): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.organizationsService.updateWarehouse(this.organizationId, editingId, { name, isActive })
      : this.organizationsService.createWarehouse(this.organizationId, { name });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save warehouse. Please try again.');
      },
    });
  }

  protected requestDelete(item: Warehouse): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: Warehouse): void {
    this.organizationsService.deleteWarehouse(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete warehouse. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load warehouses.');
      },
    });
  }
}
