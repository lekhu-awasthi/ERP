import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { VariantAttribute, VariantAttributeOption } from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';

/**
 * Phase 24 (FR-8.3) -- the tenant-global attribute catalog, confirmed live: a flat list of
 * Name + Options, whose create form is Name* plus a repeating options list and nothing else.
 *
 * Options are never deleted, only retired: existing variants point at them, so removing one would
 * strand a product's history. See VariantAttributeOption's doc comment server-side.
 */
@Component({
  selector: 'app-variant-attribute-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './variant-attribute-list-page.html',
})
export class VariantAttributeListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly catalogService = inject(CatalogService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<VariantAttribute[]>([]);
  protected readonly editingId = signal<string | null>(null);

  /** Options typed into the create form, before the attribute exists. */
  protected readonly draftOptions = signal<string[]>([]);

  /** Which attribute's inline "add option" input is open. */
  protected readonly addingOptionTo = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
  });

  protected readonly optionForm = this.fb.nonNullable.group({
    value: ['', [Validators.required, Validators.maxLength(100)]],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.draftOptions.set([]);
    this.form.reset({ name: '', isActive: true });
  }

  protected startEdit(item: VariantAttribute): void {
    this.editingId.set(item.id);
    this.draftOptions.set([]);
    this.form.reset({ name: item.name, isActive: item.isActive });
  }

  protected addDraftOption(input: HTMLInputElement): void {
    const value = input.value.trim();
    if (!value) return;

    // Reject a duplicate here as well as server-side, so the user finds out while typing rather
    // than on Save with the whole form to redo.
    if (this.draftOptions().some((x) => x.toLowerCase() === value.toLowerCase())) {
      this.errorMessage.set(`'${value}' is already in this list.`);
      return;
    }

    this.errorMessage.set(null);
    this.draftOptions.update((list) => [...list, value]);
    input.value = '';
  }

  protected removeDraftOption(value: string): void {
    this.draftOptions.update((list) => list.filter((x) => x !== value));
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    if (!editingId && this.draftOptions().length === 0) {
      this.errorMessage.set('Add at least one option -- an attribute with no options cannot produce a variant.');
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    // Explicit branch rather than a shared request$ variable: the two calls have different request
    // shapes, which trips TS2349 at the .subscribe() (see CLAUDE.md's phase-4 bug #3).
    if (editingId) {
      this.catalogService.updateVariantAttribute(this.organizationId, editingId, { name, isActive }).subscribe({
        next: () => this.onSaved(),
        error: (err: unknown) => this.onSaveError(err),
      });
    } else {
      this.catalogService
        .createVariantAttribute(this.organizationId, { name, options: this.draftOptions() })
        .subscribe({
          next: () => this.onSaved(),
          error: (err: unknown) => this.onSaveError(err),
        });
    }
  }

  protected startAddOption(item: VariantAttribute): void {
    this.addingOptionTo.set(item.id);
    this.optionForm.reset({ value: '' });
  }

  protected cancelAddOption(): void {
    this.addingOptionTo.set(null);
  }

  protected addOption(item: VariantAttribute): void {
    if (this.optionForm.invalid) {
      this.optionForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.catalogService
      .addVariantAttributeOption(this.organizationId, item.id, this.optionForm.getRawValue().value)
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.addingOptionTo.set(null);
          this.load();
        },
        error: (err: unknown) => this.onSaveError(err),
      });
  }

  protected toggleOption(item: VariantAttribute, option: VariantAttributeOption): void {
    this.saving.set(true);
    this.catalogService
      .updateVariantAttributeOption(this.organizationId, item.id, option.id, {
        value: option.value,
        isActive: !option.isActive,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.load();
        },
        error: (err: unknown) => this.onSaveError(err),
      });
  }

  private onSaved(): void {
    this.saving.set(false);
    this.startCreate();
    this.load();
  }

  private onSaveError(err: unknown): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save. Please try again.');
  }

  private load(): void {
    this.loading.set(true);
    this.catalogService.listVariantAttributes(this.organizationId).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load variant attributes.');
      },
    });
  }
}
