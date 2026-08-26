import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CostTerm, CostTermCategory } from '../../../core/configuration/configuration.models';

interface CostTermSection {
  category: CostTermCategory;
  title: string;
  blurb: string;
  emptyText: string;
  items: CostTerm[];
}

/**
 * Roadmap Phase 20c: Admin can create/edit/delete a CostTerm through this screen. One list with a
 * Category discriminator rendered as the reference product's two sections (erp-module-scan.md
 * Configurations §7) -- Additional Cost Terms (landed cost) and Production Cost Terms (BOM /
 * Production Journal, Phase 25). Nothing consumes these yet; this is reference data only.
 */
@Component({
  selector: 'app-cost-term-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './cost-term-list-page.html',
})
export class CostTermListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<CostTerm[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  /**
   * The reference product's two sections over one CostTerm shape. Derived rather than fetched
   * twice -- the list endpoint returns both categories in one bounded page (see
   * ConfigurationService.listAll), so splitting client-side costs nothing and keeps a single
   * reload path after every save/delete.
   */
  protected readonly sections = computed<readonly CostTermSection[]>(() => [
    {
      category: 'AdditionalCost',
      title: 'Additional Cost Terms',
      blurb: 'Landed-cost items, e.g. Freight, Insurance, Customs Duty.',
      emptyText: 'No additional cost terms yet. Add one using the form above.',
      items: this.items().filter((item) => item.category === 'AdditionalCost'),
    },
    {
      category: 'ProductionCost',
      title: 'Production Cost Terms',
      blurb: 'Expense terms rolled into a Bill of Materials / Production Journal cost.',
      emptyText: 'No production cost terms yet. Add one using the form above.',
      items: this.items().filter((item) => item.category === 'ProductionCost'),
    },
  ]);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    category: ['AdditionalCost' as CostTermCategory, [Validators.required]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', category: 'AdditionalCost', isActive: true });
  }

  protected startEdit(item: CostTerm): void {
    this.editingId.set(item.id);
    this.form.reset({ name: item.name, category: item.category, isActive: item.isActive });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, category, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    const request$ = editingId
      ? this.configurationService.updateCostTerm(this.organizationId, editingId, { name, category, isActive })
      : this.configurationService.createCostTerm(this.organizationId, { name, category });

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save cost term. Please try again.');
      },
    });
  }

  protected requestDelete(item: CostTerm): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: CostTerm): void {
    this.configurationService.deleteCostTerm(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete cost term. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listCostTerms(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load cost terms.');
      },
    });
  }
}
