import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductVariantPanelComponent } from '../product-variant-panel/product-variant-panel';
import {
  Product,
  ProductCategory,
  ProductSecondaryUnit,
  ProductType,
  UnitOfMeasurement,
  VatRate,
} from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';
import { BillingLocationStore } from '../../../shared/locations/billing-location-store';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/** Record-detail-page chrome for Product, mirroring contact-detail-page's shape (left
 * mini-profile + vertical tab list + right content pane). See that component's doc comment for
 * the rationale (new pattern for this codebase, Activity tab explicitly out of scope, and why
 * this subscribes to route.paramMap instead of reading route.snapshot once). */
@Component({
  selector: 'app-product-detail-page',
  imports: [ReactiveFormsModule, RouterLink, ProductVariantPanelComponent, StatusBanner],
  templateUrl: './product-detail-page.html',
})
export class ProductDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
  private readonly locationStore = inject(BillingLocationStore);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly product = signal<Product | null>(null);
  protected readonly editing = signal(false);
  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly units = signal<UnitOfMeasurement[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  private routeProductId = '';

  protected readonly addingSecondaryUnit = signal(false);
  protected readonly secondaryUnitSaving = signal(false);

  /**
   * Phase 45 -- the row being edited, and the row whose delete is being confirmed. Plain signals
   * written by their own handlers, never derived inside a `computed()` (zoneless, phase-17).
   */
  protected readonly editingSecondaryUnitId = signal<string | null>(null);
  protected readonly confirmingDeleteSecondaryUnitId = signal<string | null>(null);

  /**
   * Phase 45 -- a variant *parent* has no unit matrix. The reference product says it by giving a
   * parent's detail page no Inventory Details panel at all (no stock, no Secondary Unit tab, no
   * Warehouse tab), which is the same fact as phase 24's rule that a parent may not reach a
   * document line: it holds no stock, so there is nothing for a conversion rate to convert. The
   * server refuses it with a 409; this keeps the control from being offered in the first place.
   *
   * <p><b>...unless the parent still holds rows.</b> A product that already had secondary units can
   * be promoted to a parent afterwards, and hiding the card outright would leave those rows
   * invisible and unremovable. So a parent with stale rows still sees the table -- read-only except
   * for Delete, which is the repair.</p>
   */
  protected readonly showsSecondaryUnits = computed(
    () => this.product()?.hasVariants !== true || (this.product()?.secondaryUnits.length ?? 0) > 0,
  );

  /** True only in the stale case above: a parent that still carries rows it can no longer curate. */
  protected readonly secondaryUnitsAreStale = computed(
    () => this.product()?.hasVariants === true && (this.product()?.secondaryUnits.length ?? 0) > 0,
  );

  /**
   * ...and a variant *child* is not itself a parent, so it gets no Attributes Used editor. Live, a
   * variant child's page carries Inventory Details and no variant panel; the parent's carries the
   * variant panel and no Inventory Details. The two are exactly complementary.
   */
  protected readonly showsVariantPanel = computed(() => this.product()?.parentProductId == null);

  protected readonly categoryRows = computed<TreeRow<ProductCategory>[]>(() =>
    buildTreeRows(
      this.categories(),
      (category) => category.id,
      (category) => category.parentCategoryId,
      (category) => category.name,
    ),
  );

  /**
   * Phase 36 -- the billing locations this product is available at, as a checkbox set.
   *
   * <p>Empty is `All`, not "nowhere": the live control renders `All` when nothing is ticked and
   * carries no required marker, and the server reads an empty set as no restriction at all. Kept
   * in its own signal rather than in the reactive form, because a set of ids is not a form control
   * and the zoneless gotcha (a `computed()` over a plain `FormControl.value` caches forever) makes
   * a signal the cheaper answer.</p>
   *
   * <p>Rendered only for a tenant that actually has more than one location -- the store returns an
   * empty list for a tenant without the entitlement, so the block disappears by itself.</p>
   */
  protected readonly locations = this.locationStore.locations(this.organizationId);
  protected readonly selectedLocationIds = signal<string[]>([]);

  protected readonly types: ProductType[] = ['Goods', 'Service'];
  protected readonly vatRates: VatRate[] = ['NoVat', 'ZeroVat', 'ThirteenPercentVat'];

  protected readonly form = this.fb.nonNullable.group({
    type: ['Goods' as ProductType, Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    categoryId: ['', Validators.required],
    primaryUnitId: ['', Validators.required],
    hsCode: [''],
    availableForSale: [true],
    sellingPrice: [0, [Validators.required, Validators.min(0)]],
    purchasePrice: [0, [Validators.required, Validators.min(0)]],
    vatRate: ['NoVat' as VatRate, Validators.required],
    reOrderLevel: [0, [Validators.required, Validators.min(0)]],
    trackInventory: [true],
    // The Product screen displayed an Active/Inactive badge and sent `isActive` straight back
    // unchanged, so the status was readable and unwritable -- phase-31's rule (a field is reachable
    // only if you can name the command that writes it AND the screen that calls it) failing on the
    // screen half. UpdateProductCommand has always accepted it; nothing asked the user.
    isActive: [true],
    salesAccountId: [''],
    salesReturnAccountId: [''],
    purchaseAccountId: [''],
    purchaseReturnAccountId: [''],
  });

  protected readonly secondaryUnitForm = this.fb.nonNullable.group({
    unitId: ['', Validators.required],
    conversionRate: [1, [Validators.required, Validators.min(0.000001)]],
    sellingPrice: [0, [Validators.required, Validators.min(0)]],
    purchasePrice: [0, [Validators.required, Validators.min(0)]],
  });

  constructor() {
    this.catalogService.listProductCategories(this.organizationId).subscribe({
      next: (categories) => this.categories.set(categories),
    });
    this.catalogService.listUnitsOfMeasurement(this.organizationId).subscribe({
      next: (units) => this.units.set(units),
    });
    this.accountingService.listAllAccounts(this.organizationId).subscribe({
      next: (accounts) => this.accounts.set(accounts),
    });

    this.route.paramMap.subscribe((params) => {
      this.routeProductId = params.get('productId')!;
      const isNew = this.routeProductId === 'new';
      this.isNew.set(isNew);
      this.editing.set(isNew);
      this.product.set(null);
      this.errorMessage.set(null);
      this.addingSecondaryUnit.set(false);
      this.editingSecondaryUnitId.set(null);
      this.confirmingDeleteSecondaryUnitId.set(null);

      this.selectedLocationIds.set([]);

      if (isNew) {
        this.loading.set(false);
        this.form.reset({
          type: 'Goods',
          name: '',
          categoryId: '',
          primaryUnitId: '',
          hsCode: '',
          availableForSale: true,
          sellingPrice: 0,
          purchasePrice: 0,
          vatRate: 'NoVat',
          reOrderLevel: 0,
          trackInventory: true,
          salesAccountId: '',
          salesReturnAccountId: '',
          purchaseAccountId: '',
          purchaseReturnAccountId: '',
        });
      } else {
        this.load();
      }
    });
  }

  protected isLocationSelected(locationId: string): boolean {
    return this.selectedLocationIds().includes(locationId);
  }

  protected toggleLocation(locationId: string, selected: boolean): void {
    this.selectedLocationIds.update((ids) =>
      selected ? [...new Set([...ids, locationId])] : ids.filter((id) => id !== locationId));
  }

  protected startEdit(): void {
    const product = this.product();
    if (product) {
      this.selectedLocationIds.set((product.locations ?? []).map((x) => x.locationId));
      this.form.reset({
        type: product.type,
        name: product.name,
        categoryId: product.categoryId,
        primaryUnitId: product.primaryUnitId,
        hsCode: product.hsCode ?? '',
        availableForSale: product.availableForSale,
        sellingPrice: product.sellingPrice,
        purchasePrice: product.purchasePrice,
        vatRate: product.vatRate,
        reOrderLevel: product.reOrderLevel,
        trackInventory: product.trackInventory,
        isActive: product.isActive,
        salesAccountId: product.salesAccountId ?? '',
        salesReturnAccountId: product.salesReturnAccountId ?? '',
        purchaseAccountId: product.purchaseAccountId ?? '',
        purchaseReturnAccountId: product.purchaseReturnAccountId ?? '',
      });
    }
    this.editing.set(true);
  }

  protected cancelEdit(): void {
    if (this.isNew()) {
      this.router.navigate(['/organizations', this.organizationId, 'products']);
      return;
    }
    this.editing.set(false);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const {
      type,
      name,
      categoryId,
      primaryUnitId,
      hsCode,
      availableForSale,
      sellingPrice,
      purchasePrice,
      vatRate,
      reOrderLevel,
      trackInventory,
      isActive,
      salesAccountId,
      salesReturnAccountId,
      purchaseAccountId,
      purchaseReturnAccountId,
    } = this.form.getRawValue();
    const hsCodeOrNull = hsCode || null;

    if (this.isNew()) {
      this.catalogService
        .createProduct(this.organizationId, {
          type,
          name,
          categoryId,
          primaryUnitId,
          hsCode: hsCodeOrNull,
          availableForSale,
          sellingPrice,
          purchasePrice,
          vatRate,
          reOrderLevel,
          trackInventory,
          locationIds: this.selectedLocationIds(),
        })
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'products', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create product. Please try again.');
          },
        });
      return;
    }

    this.catalogService
      .updateProduct(this.organizationId, this.routeProductId, {
        name,
        categoryId,
        primaryUnitId,
        hsCode: hsCodeOrNull,
        availableForSale,
        sellingPrice,
        purchasePrice,
        vatRate,
        reOrderLevel,
        trackInventory,
        isActive,
        salesAccountId: salesAccountId || null,
        salesReturnAccountId: salesReturnAccountId || null,
        purchaseAccountId: purchaseAccountId || null,
        purchaseReturnAccountId: purchaseReturnAccountId || null,
        locationIds: this.selectedLocationIds(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editing.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update product. Please try again.');
        },
      });
  }

  /** What the read-only view shows: the location names, or `All` for an unrestricted product. */
  protected locationSummary(): string {
    const ids = (this.product()?.locations ?? []).map((x) => x.locationId);
    if (ids.length === 0) {
      return 'All';
    }

    return this.locations()
      .filter((location) => ids.includes(location.id))
      .map((location) => location.name)
      .join(', ');
  }

  protected categoryName(categoryId: string): string {
    return this.categories().find((c) => c.id === categoryId)?.name ?? '—';
  }

  protected unitName(unitId: string): string {
    return this.units().find((u) => u.id === unitId)?.name ?? '—';
  }

  protected accountLabel(accountId: string | null): string {
    if (!accountId) {
      return '—';
    }
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected startAddSecondaryUnit(): void {
    this.editingSecondaryUnitId.set(null);
    this.confirmingDeleteSecondaryUnitId.set(null);
    this.secondaryUnitForm.reset({ unitId: '', conversionRate: 1, sellingPrice: 0, purchasePrice: 0 });
    this.secondaryUnitForm.controls.unitId.enable();
    this.addingSecondaryUnit.set(true);
  }

  /**
   * Phase 45 -- editing reuses the same form with the unit control disabled, because the unit is
   * the row's identity: the command does not accept a new one, so offering the select would be a
   * control whose value is silently dropped.
   */
  protected startEditSecondaryUnit(row: ProductSecondaryUnit): void {
    this.addingSecondaryUnit.set(false);
    this.confirmingDeleteSecondaryUnitId.set(null);
    this.secondaryUnitForm.reset({
      unitId: row.unitId,
      conversionRate: row.conversionRate,
      sellingPrice: row.sellingPrice,
      purchasePrice: row.purchasePrice,
    });
    this.secondaryUnitForm.controls.unitId.disable();
    this.editingSecondaryUnitId.set(row.id);
  }

  protected cancelAddSecondaryUnit(): void {
    this.addingSecondaryUnit.set(false);
    this.editingSecondaryUnitId.set(null);
    this.secondaryUnitForm.controls.unitId.enable();
  }

  protected requestDeleteSecondaryUnit(row: ProductSecondaryUnit): void {
    this.confirmingDeleteSecondaryUnitId.set(row.id);
  }

  protected cancelDeleteSecondaryUnit(): void {
    this.confirmingDeleteSecondaryUnitId.set(null);
  }

  protected confirmDeleteSecondaryUnit(row: ProductSecondaryUnit): void {
    this.secondaryUnitSaving.set(true);
    this.catalogService.deleteSecondaryUnit(this.organizationId, this.routeProductId, row.id).subscribe({
      next: () => {
        this.secondaryUnitSaving.set(false);
        this.confirmingDeleteSecondaryUnitId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.secondaryUnitSaving.set(false);
        this.confirmingDeleteSecondaryUnitId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete secondary unit. Please try again.');
      },
    });
  }

  protected saveSecondaryUnit(): void {
    if (this.secondaryUnitForm.invalid) {
      this.secondaryUnitForm.markAllAsTouched();
      return;
    }

    this.secondaryUnitSaving.set(true);
    const { unitId, conversionRate, sellingPrice, purchasePrice } = this.secondaryUnitForm.getRawValue();
    const editingId = this.editingSecondaryUnitId();

    // An explicit branch rather than a shared `request$`, because the two result types differ
    // (CLAUDE.md, phase-4 bug #3).
    if (editingId) {
      this.catalogService
        .updateSecondaryUnit(this.organizationId, this.routeProductId, editingId, {
          conversionRate,
          sellingPrice,
          purchasePrice,
        })
        .subscribe({
          next: () => this.onSecondaryUnitSaved(),
          error: (err: unknown) =>
            this.onSecondaryUnitError(err, 'Could not save secondary unit. Please try again.'),
        });
      return;
    }

    this.catalogService.addSecondaryUnit(this.organizationId, this.routeProductId, {
      unitId,
      conversionRate,
      sellingPrice,
      purchasePrice,
    }).subscribe({
      next: () => this.onSecondaryUnitSaved(),
      error: (err: unknown) =>
        this.onSecondaryUnitError(err, 'Could not add secondary unit. Please try again.'),
    });
  }

  /** The server refuses both cases with a 409; the picker simply does not offer them. */
  protected unitAlreadyUsed(unitId: string): boolean {
    const product = this.product();
    if (!product) {
      return false;
    }
    return unitId === product.primaryUnitId || product.secondaryUnits.some((su) => su.unitId === unitId);
  }

  private onSecondaryUnitSaved(): void {
    this.secondaryUnitSaving.set(false);
    this.addingSecondaryUnit.set(false);
    this.editingSecondaryUnitId.set(null);
    this.secondaryUnitForm.controls.unitId.enable();
    this.load();
  }

  private onSecondaryUnitError(err: unknown, fallback: string): void {
    this.secondaryUnitSaving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }

  private load(): void {
    this.loading.set(true);
    this.catalogService.getProduct(this.organizationId, this.routeProductId).subscribe({
      next: (product) => {
        this.product.set(product);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load product.');
      },
    });
  }
}
