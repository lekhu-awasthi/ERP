import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  ProductVariant,
  ProductVariantPanel,
  VariantAttribute,
  VariantCombinationInput,
} from '../../../core/catalog/catalog.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';

/**
 * Phase 24 (FR-8.3) -- the "Attributes Used" + "Variant Details" panel, laid out as the live
 * reference product lays it out (confirmed in the browser): a per-attribute chip row of the options
 * this product offers, then a table of its actual variants.
 *
 * Two creation affordances, deliberately. **+ Add** is the live product's own flow -- one variant
 * at a time, picking one option per attribute; its "Iphone 16 Pro Max" offers 4 colours x 3 sizes
 * and carries exactly 4 variants. **Generate** fills the whole matrix, which FR-8.3 and the
 * roadmap's exit criterion ask for and the reference product does not have. Re-running Generate is
 * safe: existing combinations are skipped, not duplicated.
 *
 * Note the app has no Bootstrap JS (CLAUDE.md's gotcha), so every open/close state here is a signal.
 */
@Component({
  selector: 'app-product-variant-panel',
  imports: [ReactiveFormsModule, AmountPipe],
  templateUrl: './product-variant-panel.html',
})
export class ProductVariantPanelComponent implements OnInit {
  private readonly catalogService = inject(CatalogService);
  private readonly fb = inject(FormBuilder);

  readonly organizationId = input.required<string>();
  readonly productId = input.required<string>();
  readonly productName = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly panel = signal<ProductVariantPanel | null>(null);
  protected readonly catalog = signal<VariantAttribute[]>([]);

  protected readonly editingAttributes = signal(false);
  protected readonly addingVariant = signal(false);
  protected readonly editingVariantId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  /**
   * The option ids ticked in the Attributes Used editor. A plain signal, written directly by the
   * checkbox handler -- NOT derived from a FormControl inside computed(), which this zoneless app
   * silently caches forever (CLAUDE.md's zoneless-computed gotcha).
   */
  protected readonly selectedOptionIds = signal<ReadonlySet<string>>(new Set());

  /** The option chosen per attribute in the "+ Add" form, keyed by attribute id. Same reasoning. */
  protected readonly chosenOptions = signal<Record<string, string>>({});

  protected readonly variantForm = this.fb.nonNullable.group({
    name: ['', [Validators.maxLength(200)]],
    sku: ['', [Validators.maxLength(60)]],
    barcode: ['', [Validators.maxLength(60)]],
    sellingPrice: [0, [Validators.required, Validators.min(0)]],
    purchasePrice: [0, [Validators.min(0)]],
    isActive: [true],
  });

  protected readonly hasVariants = computed(() => (this.panel()?.variants.length ?? 0) > 0);

  protected readonly attributesUsed = computed(() => this.panel()?.attributesUsed ?? []);

  /** How many variants a Generate would produce, so the button can say so before it is pressed. */
  protected readonly matrixSize = computed(() => {
    const used = this.attributesUsed();
    if (used.length === 0) return 0;
    return used.reduce((total, attribute) => total * attribute.options.length, 1);
  });

  /** input() values are not readable in a field initializer, but they are by ngOnInit. */
  ngOnInit(): void {
    this.load();
  }

  // ---- Attributes Used ----

  protected startEditAttributes(): void {
    this.errorMessage.set(null);
    this.notice.set(null);
    this.selectedOptionIds.set(
      new Set(this.attributesUsed().flatMap((a) => a.options.map((o) => o.optionId))),
    );
    this.editingAttributes.set(true);

    this.catalogService.listVariantAttributes(this.organizationId(), true).subscribe({
      next: (result) => this.catalog.set(result.items),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load variant attributes.'),
    });
  }

  protected cancelEditAttributes(): void {
    this.editingAttributes.set(false);
  }

  protected isOptionSelected(optionId: string): boolean {
    return this.selectedOptionIds().has(optionId);
  }

  protected toggleOption(optionId: string): void {
    this.selectedOptionIds.update((current) => {
      const next = new Set(current);
      if (!next.delete(optionId)) next.add(optionId);
      return next;
    });
  }

  protected saveAttributes(): void {
    const selected = this.selectedOptionIds();
    const usages: VariantCombinationInput[] = this.catalog()
      .flatMap((attribute) =>
        attribute.options
          .filter((option) => selected.has(option.id))
          .map((option) => ({ attributeId: attribute.id, optionId: option.id })),
      );

    this.saving.set(true);
    this.errorMessage.set(null);

    this.catalogService.setProductVariantAttributes(this.organizationId(), this.productId(), usages).subscribe({
      next: () => {
        this.saving.set(false);
        this.editingAttributes.set(false);
        this.load();
      },
      error: (err: unknown) => this.onError(err, 'Could not save the attributes.'),
    });
  }

  // ---- variants ----

  protected startAddVariant(): void {
    this.errorMessage.set(null);
    this.notice.set(null);
    this.editingVariantId.set(null);
    this.chosenOptions.set({});
    this.variantForm.reset({ name: '', sku: '', barcode: '', sellingPrice: 0, purchasePrice: 0, isActive: true });
    this.addingVariant.set(true);
  }

  protected cancelVariantForm(): void {
    this.addingVariant.set(false);
    this.editingVariantId.set(null);
  }

  protected chooseOption(attributeId: string, event: Event): void {
    const optionId = (event.target as HTMLSelectElement).value;
    this.chosenOptions.update((current) => ({ ...current, [attributeId]: optionId }));
  }

  protected chosenOption(attributeId: string): string {
    return this.chosenOptions()[attributeId] ?? '';
  }

  protected saveVariant(): void {
    if (this.variantForm.invalid) {
      this.variantForm.markAllAsTouched();
      return;
    }

    const { name, sku, barcode, sellingPrice, purchasePrice, isActive } = this.variantForm.getRawValue();
    const editingId = this.editingVariantId();

    this.saving.set(true);
    this.errorMessage.set(null);

    // Explicit branch, not a shared request$ (CLAUDE.md's phase-4 bug #3).
    if (editingId) {
      this.catalogService
        .updateProductVariant(this.organizationId(), this.productId(), editingId, {
          name,
          sku: sku || null,
          barcode: barcode || null,
          sellingPrice,
          purchasePrice,
          isActive,
        })
        .subscribe({
          next: () => this.onVariantSaved(),
          error: (err: unknown) => this.onError(err, 'Could not save the variant.'),
        });
      return;
    }

    const chosen = this.chosenOptions();
    const combination: VariantCombinationInput[] = this.attributesUsed()
      .filter((attribute) => chosen[attribute.attributeId])
      .map((attribute) => ({ attributeId: attribute.attributeId, optionId: chosen[attribute.attributeId] }));

    if (combination.length !== this.attributesUsed().length) {
      this.saving.set(false);
      this.errorMessage.set('Pick one option for every attribute.');
      return;
    }

    this.catalogService
      .createProductVariant(this.organizationId(), this.productId(), {
        combination,
        name: name || null,
        sku: sku || null,
        barcode: barcode || null,
        sellingPrice,
        purchasePrice,
      })
      .subscribe({
        next: () => this.onVariantSaved(),
        error: (err: unknown) => this.onError(err, 'Could not create the variant.'),
      });
  }

  protected startEditVariant(variant: ProductVariant): void {
    this.errorMessage.set(null);
    this.notice.set(null);
    this.addingVariant.set(false);
    this.editingVariantId.set(variant.id);
    this.variantForm.reset({
      name: variant.name,
      sku: variant.sku ?? '',
      barcode: variant.barcode ?? '',
      sellingPrice: variant.sellingPrice,
      purchasePrice: variant.purchasePrice,
      isActive: variant.isActive,
    });
  }

  protected generate(): void {
    this.saving.set(true);
    this.errorMessage.set(null);
    this.notice.set(null);

    this.catalogService.generateProductVariants(this.organizationId(), this.productId()).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.notice.set(
          result.created.length === 0
            ? `Nothing to add — all ${result.skippedExisting} combination(s) already exist.`
            : `Added ${result.created.length} variant(s)` +
              (result.skippedExisting > 0 ? `, skipped ${result.skippedExisting} that already existed.` : '.'),
        );
        this.load();
      },
      error: (err: unknown) => this.onError(err, 'Could not generate variants.'),
    });
  }

  protected requestDelete(variant: ProductVariant): void {
    this.confirmingDeleteId.set(variant.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(variant: ProductVariant): void {
    this.saving.set(true);
    this.catalogService.deleteProductVariant(this.organizationId(), this.productId(), variant.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.onError(err, 'Could not delete the variant.');
      },
    });
  }

  protected combinationLabel(variant: ProductVariant): string {
    return variant.attributeValues.map((v) => `${v.attributeName}: ${v.optionValue}`).join(' · ');
  }

  private onVariantSaved(): void {
    this.saving.set(false);
    this.addingVariant.set(false);
    this.editingVariantId.set(null);
    this.load();
  }

  private onError(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private load(): void {
    this.loading.set(true);
    this.catalogService.getProductVariants(this.organizationId(), this.productId()).subscribe({
      next: (panel) => {
        this.panel.set(panel);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load variants.');
      },
    });
  }
}
