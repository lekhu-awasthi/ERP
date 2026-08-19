import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { Account, CashTransferDetail, CashTransferLineInput } from '../../../core/accounting/accounting.models';

interface EditableLine {
  key: number;
  toAccountId: string;
  amount: number;
}

let nextLineKey = 1;

/** Simplified fan-out variant of journal-voucher-detail-page's chrome -- one FromAccountId header
 * field, N (ToAccount, Amount) destination lines instead of a Debit/Credit column pair. Still
 * posts through the same GlJournalEntry.Post path (see CashTransferPostingRule), so the same
 * live client-side Total is meaningful here even though there's no "Difference" to show (a
 * fan-out transfer is balanced by construction: total-out always equals the From account's
 * credit). */
@Component({
  selector: 'app-cash-transfer-detail-page',
  imports: [RouterLink, DatePipe],
  templateUrl: './cash-transfer-detail-page.html',
})
export class CashTransferDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly approving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly cashTransfer = signal<CashTransferDetail | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly isNew = signal(false);

  protected readonly date = signal(this.today());
  protected readonly reference = signal('');
  protected readonly fromAccountId = signal('');
  protected readonly lines = signal<EditableLine[]>([]);

  private routeCashTransferId = '';

  protected readonly totalAmount = computed(() => this.round(this.lines().reduce((sum, l) => sum + (l.amount || 0), 0)));

  protected readonly isDraft = computed(() => {
    const cashTransfer = this.cashTransfer();
    return this.isNew() || !cashTransfer || cashTransfer.status === 'Draft';
  });

  protected readonly canApprove = computed(() => {
    const lines = this.lines();
    const fromAccountId = this.fromAccountId();
    return (
      !this.isNew() &&
      !!fromAccountId &&
      lines.length >= 1 &&
      lines.every((l) => l.toAccountId && l.toAccountId !== fromAccountId && l.amount > 0)
    );
  });

  protected readonly sortedAccounts = computed(() => [...this.accounts()].sort((a, b) => a.code.localeCompare(b.code)));

  constructor() {
    this.accountingService.listAccounts(this.organizationId).subscribe({ next: (accounts) => this.accounts.set(accounts) });

    this.route.paramMap.subscribe((params) => {
      this.routeCashTransferId = params.get('cashTransferId')!;
      const isNew = this.routeCashTransferId === 'new';
      this.isNew.set(isNew);
      this.cashTransfer.set(null);
      this.errorMessage.set(null);

      if (isNew) {
        this.loading.set(false);
        this.date.set(this.today());
        this.reference.set('');
        this.fromAccountId.set('');
        this.lines.set([this.newLine()]);
      } else {
        this.load();
      }
    });
  }

  protected accountLabel(accountId: string): string {
    const account = this.accounts().find((a) => a.id === accountId);
    return account ? `${account.code} — ${account.name}` : '—';
  }

  protected onToAccountChange(key: number, event: Event): void {
    const toAccountId = (event.target as HTMLSelectElement).value;
    this.updateLine(key, { toAccountId });
  }

  protected onAmountChange(key: number, event: Event): void {
    const amount = (event.target as HTMLInputElement).valueAsNumber;
    this.updateLine(key, { amount: Number.isFinite(amount) ? amount : 0 });
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, this.newLine()]);
  }

  protected removeLine(key: number): void {
    this.lines.update((lines) => lines.filter((l) => l.key !== key));
  }

  protected saveDraft(): void {
    const fromAccountId = this.fromAccountId();
    if (!fromAccountId) {
      this.errorMessage.set('Select a From Account.');
      return;
    }

    const lines = this.toLineInputs();
    if (!lines) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const request = { date: this.date(), reference: this.reference() || null, fromAccountId, lines };
    const request$ = this.isNew()
      ? this.accountingService.createCashTransfer(this.organizationId, request)
      : this.accountingService.updateCashTransfer(this.organizationId, this.routeCashTransferId, request);

    request$.subscribe({
      next: (result) => {
        this.saving.set(false);
        if (this.isNew()) {
          this.router.navigate(['/organizations', this.organizationId, 'accounting', 'cash-transfers', result.id]);
        } else {
          this.load();
        }
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save cash transfer. Please try again.');
      },
    });
  }

  protected voidCashTransfer(): void {
    if (!window.confirm('Void this cash transfer? This reverses its GL posting and cannot be undone.')) {
      return;
    }

    this.voiding.set(true);
    this.errorMessage.set(null);

    this.accountingService.voidCashTransfer(this.organizationId, this.routeCashTransferId).subscribe({
      next: () => {
        this.voiding.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.voiding.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not void cash transfer. Please try again.');
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.errorMessage.set(null);

    this.accountingService.approveCashTransfer(this.organizationId, this.routeCashTransferId).subscribe({
      next: () => {
        this.approving.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.approving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not approve cash transfer. Please try again.');
      },
    });
  }

  private toLineInputs(): CashTransferLineInput[] | null {
    const lines = this.lines()
      .filter((l) => l.toAccountId && l.amount > 0)
      .map((l) => ({ toAccountId: l.toAccountId, amount: l.amount }));

    if (lines.length === 0) {
      this.errorMessage.set('Add at least one destination line with an Account and an Amount.');
      return null;
    }

    return lines;
  }

  private updateLine(key: number, patch: Partial<Pick<EditableLine, 'toAccountId' | 'amount'>>): void {
    this.lines.update((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  private newLine(): EditableLine {
    return { key: nextLineKey++, toAccountId: '', amount: 0 };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private round(value: number): number {
    return Math.round(value * 100) / 100;
  }

  private load(): void {
    this.loading.set(true);
    this.accountingService.getCashTransfer(this.organizationId, this.routeCashTransferId).subscribe({
      next: (cashTransfer) => {
        this.cashTransfer.set(cashTransfer);
        this.date.set(cashTransfer.date);
        this.reference.set(cashTransfer.reference ?? '');
        this.fromAccountId.set(cashTransfer.fromAccountId);
        this.lines.set(
          cashTransfer.lines.length > 0
            ? cashTransfer.lines.map((l) => ({ key: nextLineKey++, toAccountId: l.toAccountId, amount: l.amount }))
            : [this.newLine()],
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load cash transfer.');
      },
    });
  }
}
