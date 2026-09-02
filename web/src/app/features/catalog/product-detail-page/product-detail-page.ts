import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { ProductVariantPanelComponent } from '../product-variant-panel/product-variant-panel';
import { Product, ProductCategory, ProductType, UnitOfMeasurement, VatRate } from '../../../core/catalog/catalog.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account } from '../../../core/accounting/accounting.models';

/** Record-detail-page chrome for Product, mirroring contact-detail-page's shape (left
 * mini-profile + vertical tab list + right content pane). See that component's doc comment for
 * the rationale (new pattern for this codebase, Activity tab explicitly out of scope, and why
 * this subscribes to route.paramMap instead of reading route.snapshot once). */
@Component({
  selector: 'app-product-detail-page',
  imports: [ReactiveFormsModule, RouterLink, ProductVariantPanelComponent],
  templateUrl: './product-detail-page.html',
})
export class ProductDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalogService = inject(CatalogService);
  private readonly accountingService = inject(AccountingService);
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

  protected readonly categoryRows = computed<TreeRow<ProductCategory>[]>(() =>
    buildTreeRows(
      this.categories(),
      (category) => category.id,
      (category) => category.parentCategoryId,
      (category) => category.name,
    ),
  );

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

  protected startEdit(): void {
    const product = this.product();
    if (product) {
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
        isActive: this.product()?.isActive ?? true,
        salesAccountId: salesAccountId || null,
        salesReturnAccountId: salesReturnAccountId || null,
        purchaseAccountId: purchaseAccountId || null,
        purchaseReturnAccountId: purchaseReturnAccountId || null,
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
    this.secondaryUnitForm.reset({ unitId: '', conversionRate: 1, sellingPrice: 0, purchasePrice: 0 });
    this.addingSecondaryUnit.set(true);
  }

  protected cancelAddSecondaryUnit(): void {
    this.addingSecondaryUnit.set(false);
  }

  protected saveSecondaryUnit(): void {
    if (this.secondaryUnitForm.invalid) {
      this.secondaryUnitForm.markAllAsTouched();
      return;
    }

    this.secondaryUnitSaving.set(true);
    const { unitId, conversionRate, sellingPrice, purchasePrice } = this.secondaryUnitForm.getRawValue();

    this.catalogService.addSecondaryUnit(this.organizationId, this.routeProductId, {
      unitId,
      conversionRate,
      sellingPrice,
      purchasePrice,
    }).subscribe({
      next: () => {
        this.secondaryUnitSaving.set(false);
        this.addingSecondaryUnit.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.secondaryUnitSaving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not add secondary unit. Please try again.');
      },
    });
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
