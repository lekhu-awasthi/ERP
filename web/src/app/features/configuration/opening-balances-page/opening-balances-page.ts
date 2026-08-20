import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { AccountOpeningBalanceDto } from '../../../core/accounting/accounting.models';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { ProductOpeningBalanceDto } from '../../../core/inventory/inventory.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type OpeningBalanceTab = 'account' | 'product';
type DrCr = 'DR' | 'CR';

/** Phase 17 (Configurations §18, docs/phase-17-status.md) -- "day zero" Account and Product
 * balances. No Location/Currency fields -- live-confirmed against the Tigg reference product's
 * own screen showing neither in this tenant (Location entitlement off); Product is scoped by this
 * codebase's own first-class Warehouse dimension instead. */
@Component({
  selector: 'app-opening-balances-page',
  imports: [RouterLink, PaginationControl, DecimalPipe],
  templateUrl: './opening-balances-page.html',
})
export class OpeningBalancesPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);
  private readonly inventoryService = inject(InventoryService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly activeTab = signal<OpeningBalanceTab>('account');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly accountItems = signal<AccountOpeningBalanceDto[]>([]);
  protected readonly productItems = signal<ProductOpeningBalanceDto[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly warehouseId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly editingAccountId = signal<string | null>(null);
  protected readonly editAmount = signal(0);
  protected readonly editDrCr = signal<DrCr>('DR');

  protected readonly editingProductId = signal<string | null>(null);
  protected readonly editQuantity = signal(0);
  protected readonly editRate = signal(0);

  constructor() {
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (w) => {
        this.warehouses.set(w);
        if (w.length > 0 && !this.warehouseId()) {
          this.warehouseId.set(w[0].id);
        }
        if (this.activeTab() === 'product') {
          this.loadProducts();
        }
      },
    });
    this.loadAccounts();
  }

  protected switchTab(tab: OpeningBalanceTab): void {
    this.activeTab.set(tab);
    this.page.set(1);
    this.editingAccountId.set(null);
    this.editingProductId.set(null);
    if (tab === 'account') {
      this.loadAccounts();
    } else {
      this.loadProducts();
    }
  }

  protected onWarehouseChange(warehouseId: string): void {
    this.warehouseId.set(warehouseId);
    this.page.set(1);
    this.loadProducts();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    if (this.activeTab() === 'account') this.loadAccounts();
    else this.loadProducts();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    if (this.activeTab() === 'account') this.loadAccounts();
    else this.loadProducts();
  }

  protected startEditAccount(row: AccountOpeningBalanceDto): void {
    this.editingAccountId.set(row.accountId);
    this.editAmount.set(row.debit > 0 ? row.debit : row.credit);
    this.editDrCr.set(row.credit > 0 ? 'CR' : 'DR');
  }

  protected cancelEditAccount(): void {
    this.editingAccountId.set(null);
  }

  protected saveAccount(row: AccountOpeningBalanceDto): void {
    const amount = this.editAmount();
    if (amount <= 0) return;

    this.saving.set(true);
    this.errorMessage.set(null);
    this.accountingService
      .saveAccountOpeningBalance(this.organizationId, row.accountId, {
        debit: this.editDrCr() === 'DR' ? amount : 0,
        credit: this.editDrCr() === 'CR' ? amount : 0,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editingAccountId.set(null);
          this.loadAccounts();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save opening balance. Please try again.');
        },
      });
  }

  protected startEditProduct(row: ProductOpeningBalanceDto): void {
    this.editingProductId.set(row.productId);
    this.editQuantity.set(row.quantity);
    this.editRate.set(row.rate);
  }

  protected cancelEditProduct(): void {
    this.editingProductId.set(null);
  }

  protected saveProduct(row: ProductOpeningBalanceDto): void {
    const quantity = this.editQuantity();
    const rate = this.editRate();
    if (quantity <= 0) return;

    this.saving.set(true);
    this.errorMessage.set(null);
    this.inventoryService
      .saveProductOpeningBalance(this.organizationId, row.productId, { warehouseId: this.warehouseId(), quantity, rate })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editingProductId.set(null);
          this.loadProducts();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save opening stock. Please try again.');
        },
      });
  }

  private loadAccounts(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.accountingService.listAccountOpeningBalances(this.organizationId, this.page(), this.pageSize()).subscribe({
      next: (result) => {
        this.accountItems.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load account opening balances.');
      },
    });
  }

  private loadProducts(): void {
    if (!this.warehouseId()) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.errorMessage.set(null);
    this.inventoryService
      .listProductOpeningBalances(this.organizationId, this.warehouseId(), this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.productItems.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load product opening balances.');
        },
      });
  }
}
