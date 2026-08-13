import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { InventoryService } from '../../../core/inventory/inventory.service';
import { WarehouseTransfer, WarehouseTransferStatus } from '../../../core/inventory/inventory.models';

type StatusFilter = WarehouseTransferStatus | 'All';

/** List-page chrome for WarehouseTransfer, same pattern as purchase-order-list-page. */
@Component({
  selector: 'app-warehouse-transfer-list-page',
  imports: [RouterLink],
  templateUrl: './warehouse-transfer-list-page.html',
})
export class WarehouseTransferListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly inventoryService = inject(InventoryService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<WarehouseTransfer[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  constructor() {
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.inventoryService.listWarehouseTransfers(this.organizationId, status === 'All' ? undefined : status).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load warehouse transfers.');
      },
    });
  }
}
