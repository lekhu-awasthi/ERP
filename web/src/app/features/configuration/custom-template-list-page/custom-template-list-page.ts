import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CustomTemplate, CustomTemplateType } from '../../../core/configuration/configuration.models';

interface CustomTemplateSection {
  type: CustomTemplateType;
  title: string;
  items: CustomTemplate[];
}

const SECTION_TITLES: Record<CustomTemplateType, string> = {
  CustomerBalanceConfirmation: 'Customer Balance Confirmation',
  SupplierBalanceConfirmation: 'Supplier Balance Confirmation',
  TermsAndConditions: 'Terms and Conditions',
  Email: 'Email',
};

/**
 * Roadmap Phase 20d: Admin can create/edit merge-field text templates across the reference
 * product's four confirmed types (erp-module-scan.md §13) and choose which one is default per
 * type. Four sections over one CustomTemplate shape, same split CostTermListPage uses for its own
 * two categories.
 */
@Component({
  selector: 'app-custom-template-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './custom-template-list-page.html',
})
export class CustomTemplateListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<CustomTemplate[]>([]);
  protected readonly editingId = signal<string | null>(null);

  protected readonly sections = computed<readonly CustomTemplateSection[]>(() =>
    (Object.keys(SECTION_TITLES) as CustomTemplateType[]).map((type) => ({
      type,
      title: SECTION_TITLES[type],
      items: this.items().filter((item) => item.type === type),
    })),
  );

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    type: ['Email' as CustomTemplateType, [Validators.required]],
    body: ['', [Validators.required, Validators.maxLength(4000)]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(type: CustomTemplateType = 'Email'): void {
    this.editingId.set(null);
    this.form.reset({ name: '', type, body: '', isActive: true });
  }

  protected startEdit(item: CustomTemplate): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, type: item.type, body: item.body, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, type, body, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updateCustomTemplate(this.organizationId, editingId, { name, type, body, isActive })
      : this.configurationService.createCustomTemplate(this.organizationId, { name, type, body });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate(type);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save custom template. Please try again.');
      },
    });
  }

  protected setDefault(item: CustomTemplate): void {
    this.configurationService.setDefaultCustomTemplate(this.organizationId, item.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not set default custom template. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listCustomTemplates(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load custom templates.');
      },
    });
  }
}
