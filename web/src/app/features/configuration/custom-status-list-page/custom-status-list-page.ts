import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CustomStatus } from '../../../core/configuration/configuration.models';
import { DocumentType } from '../../../core/sales/sales.models';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { LookupFilter, matchesLookup } from '../../../shared/pagination/lookup-filter';

interface CustomStatusSection {
  documentType: DocumentType;
  title: string;
  items: CustomStatus[];
}

/**
 * The editor phase 20b never built.
 *
 * <p>That phase shipped the per-row Custom Status picker on four list grids and no way to define an
 * option, so on every tenant the dropdown read "Select Status" and offered nothing — a control whose
 * data had no write path, which is phase-31's rule ("a field is reachable only if you can name the
 * command that writes it <i>and</i> the screen that calls it") failing on the screen half. The API
 * has had full CRUD since phase 2's generic lookups; only this screen and three service methods
 * were missing.</p>
 *
 * <p><b>Grouped by document type, because that is what a status is scoped to.</b> A status belongs
 * to exactly one type and only ever appears in that type's grid, so a flat list would make the most
 * important column the easiest one to misread. Same derived-sections shape as
 * {@link CostTermListPage}, for the same reason: one bounded list request, split client-side, one
 * reload path after every save.</p>
 *
 * <p><b>Only the four types that actually render a picker are offered.</b> The server's validator
 * accepts any <c>DocumentType</c>, so a status could be created for Invoice — and it would then be
 * dead data, visible on no screen. Offering the four is this codebase's own rule about controls that
 * name things which do not render; the wider server contract is left alone rather than narrowed
 * under a UI change.</p>
 */
@Component({
  selector: 'app-custom-status-list-page',
  imports: [ReactiveFormsModule, RouterLink, StatusBanner, LookupFilter],
  templateUrl: './custom-status-list-page.html',
})
export class CustomStatusListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<CustomStatus[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  /** Client-side, because `listAll` already fetched every row — the same choice Cost Terms makes. */
  protected readonly term = signal('');

  /**
   * The document types whose list grid carries a Custom Status column. Adding a fifth means adding
   * the picker to that grid in the same change, or this screen would offer a status nothing shows.
   */
  protected readonly documentTypes: readonly { value: DocumentType; label: string }[] = [
    { value: 'Quotation', label: 'Quotation' },
    { value: 'SalesOrder', label: 'Sales Order' },
    { value: 'PurchaseOrder', label: 'Purchase Order' },
    { value: 'ProductionOrder', label: 'Production Order' },
  ];

  protected readonly filtered = computed(() =>
    this.items().filter((item) => matchesLookup(this.term(), item.name)));

  protected readonly sections = computed<readonly CustomStatusSection[]>(() =>
    this.documentTypes.map((type) => ({
      documentType: type.value,
      title: type.label,
      items: this.filtered().filter((item) => item.documentType === type.value),
    })));

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    documentType: ['Quotation' as DocumentType, [Validators.required]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', documentType: 'Quotation', isActive: true });
  }

  protected startEdit(item: CustomStatus): void {
    this.editingId.set(item.id);
    this.confirmingDeleteId.set(null);
    this.form.reset({ name: item.name, documentType: item.documentType, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, documentType, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    // Explicit branch rather than a shared `request$`: the two calls differ in shape, and phase-4
    // bug #3 is what sharing one variable across them cost last time.
    if (editingId) {
      this.configurationService
        .updateCustomStatus(this.organizationId, editingId, { name, documentType, isActive })
        .subscribe({
          next: () => this.onSaved(),
          error: (err: unknown) => this.onError(err, 'Could not save status. Please try again.'),
        });
      return;
    }

    this.configurationService
      .createCustomStatus(this.organizationId, { name, documentType })
      .subscribe({
        next: () => this.onSaved(),
        error: (err: unknown) => this.onError(err, 'Could not save status. Please try again.'),
      });
  }

  protected requestDelete(item: CustomStatus): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: CustomStatus): void {
    this.configurationService.deleteCustomStatus(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete status. Please try again.');
      },
    });
  }

  private onSaved(): void {
    this.saving.set(false);
    this.startCreate();
    this.load();
  }

  private onError(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listCustomStatuses(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load statuses.');
      },
    });
  }
}
