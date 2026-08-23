import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ReportingTagCategory, ReportingTagOption } from '../../../core/configuration/configuration.models';

/**
 * Roadmap Phase 19 gap-fill: the backend CRUD (Create/UpdateReportingTagCategory/Option,
 * DeleteLookupCommand<T>) has existed since Phase 2 but never got an Angular screen -- see
 * CLAUDE.md's Phase 19 note. The reference product's own screen (erp-module-scan.md line 329-330,
 * "+ADD NEW REPORTING TAG" = category name + option list) is one combined screen, not two
 * standalone lookup pages -- mirrored here as a category list where each row expands to manage
 * that category's options inline, rather than a separate Options page with a category picker.
 */
@Component({
  selector: 'app-reporting-tag-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reporting-tag-list-page.html',
})
export class ReportingTagListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly categories = signal<ReportingTagCategory[]>([]);
  protected readonly options = signal<ReportingTagOption[]>([]);

  protected readonly categoryEditingId = signal<string | null>(null);
  protected readonly categoryConfirmingDeleteId = signal<string | null>(null);

  protected readonly expandedCategoryId = signal<string | null>(null);
  protected readonly optionSaving = signal(false);
  protected readonly optionEditingId = signal<string | null>(null);
  protected readonly optionConfirmingDeleteId = signal<string | null>(null);

  protected readonly categoryForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
  });

  protected readonly optionForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected optionsForCategory(categoryId: string): ReportingTagOption[] {
    return this.options().filter((option) => option.categoryId === categoryId);
  }

  protected startCreateCategory(): void {
    this.categoryEditingId.set(null);
    this.categoryForm.reset({ name: '', isActive: true });
  }

  protected startEditCategory(category: ReportingTagCategory): void {
    this.categoryEditingId.set(category.id);
    this.categoryForm.reset({ name: category.name, isActive: category.isActive });
  }

  protected saveCategory(): void {
    if (this.categoryForm.invalid) {
      this.categoryForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, isActive } = this.categoryForm.getRawValue();
    const editingId = this.categoryEditingId();

    const request$ = editingId
      ? this.configurationService.updateReportingTagCategory(this.organizationId, editingId, { name, isActive })
      : this.configurationService.createReportingTagCategory(this.organizationId, { name });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreateCategory();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save reporting tag category. Please try again.');
      },
    });
  }

  protected requestDeleteCategory(category: ReportingTagCategory): void {
    this.categoryConfirmingDeleteId.set(category.id);
  }

  protected cancelDeleteCategory(): void {
    this.categoryConfirmingDeleteId.set(null);
  }

  protected confirmDeleteCategory(category: ReportingTagCategory): void {
    this.configurationService.deleteReportingTagCategory(this.organizationId, category.id).subscribe({
      next: () => {
        this.categoryConfirmingDeleteId.set(null);
        if (this.expandedCategoryId() === category.id) {
          this.expandedCategoryId.set(null);
        }
        this.load();
      },
      error: (err: unknown) => {
        this.categoryConfirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete reporting tag category. Please try again.');
      },
    });
  }

  protected toggleExpanded(category: ReportingTagCategory): void {
    const next = this.expandedCategoryId() === category.id ? null : category.id;
    this.expandedCategoryId.set(next);
    this.startCreateOption();
  }

  protected startCreateOption(): void {
    this.optionEditingId.set(null);
    this.optionForm.reset({ name: '', isActive: true });
  }

  protected startEditOption(option: ReportingTagOption): void {
    this.optionEditingId.set(option.id);
    this.optionForm.reset({ name: option.name, isActive: option.isActive });
  }

  protected saveOption(): void {
    const categoryId = this.expandedCategoryId();
    if (!categoryId) {
      return;
    }

    if (this.optionForm.invalid) {
      this.optionForm.markAllAsTouched();
      return;
    }

    this.optionSaving.set(true);
    this.errorMessage.set(null);

    const { name, isActive } = this.optionForm.getRawValue();
    const editingId = this.optionEditingId();

    const request$ = editingId
      ? this.configurationService.updateReportingTagOption(this.organizationId, editingId, { name, categoryId, isActive })
      : this.configurationService.createReportingTagOption(this.organizationId, { name, categoryId });

    request$.subscribe({
      next: () => {
        this.optionSaving.set(false);
        this.startCreateOption();
        this.load();
      },
      error: (err: unknown) => {
        this.optionSaving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save reporting tag option. Please try again.');
      },
    });
  }

  protected requestDeleteOption(option: ReportingTagOption): void {
    this.optionConfirmingDeleteId.set(option.id);
  }

  protected cancelDeleteOption(): void {
    this.optionConfirmingDeleteId.set(null);
  }

  protected confirmDeleteOption(option: ReportingTagOption): void {
    this.configurationService.deleteReportingTagOption(this.organizationId, option.id).subscribe({
      next: () => {
        this.optionConfirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.optionConfirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete reporting tag option. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listReportingTagCategories(this.organizationId).subscribe({
      next: (categories) => {
        this.categories.set(categories);
        this.configurationService.listReportingTagOptions(this.organizationId).subscribe({
          next: (options) => {
            this.options.set(options);
            this.loading.set(false);
          },
          error: (err: unknown) => {
            this.loading.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load reporting tag options.');
          },
        });
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load reporting tag categories.');
      },
    });
  }
}
