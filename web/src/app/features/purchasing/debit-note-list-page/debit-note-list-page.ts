import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { DebitNote, DebitNoteStatus } from '../../../core/purchasing/purchasing.models';

type StatusFilter = DebitNoteStatus | 'All';

@Component({
  selector: 'app-debit-note-list-page',
  imports: [RouterLink],
  templateUrl: './debit-note-list-page.html',
})
export class DebitNoteListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<DebitNote[]>([]);
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
    this.purchasingService.listDebitNotes(this.organizationId, status === 'All' ? undefined : status).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load debit notes.');
      },
    });
  }
}
