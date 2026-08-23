import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CrmService } from '../../../core/crm/crm.service';
import {
  SmsCreditLedgerRowDto,
  SmsLogRowDto,
  SmsTemplateRowDto,
} from '../../../core/crm/crm.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { SendSmsForm } from '../send-sms-form/send-sms-form';

type SmsTab = 'Overview' | 'History' | 'Templates' | 'CreditHistory';

/**
 * SMS module shell (roadmap Phase 18) -- 4 tabs confirmed live against the Tigg reference product:
 * Overview (credit balance + Recent SMS table + a Send button), SMS History (flat list), Templates
 * (list + create/edit inline form), Credit History (the ledger list + a simple Adjust Credit
 * form -- no payment UI at all, Tigg's own "Add SMS Credit" is just a "call us" tooltip).
 */
@Component({
  selector: 'app-sms-shell-page',
  imports: [ReactiveFormsModule, PaginationControl, SendSmsForm, DatePipe],
  templateUrl: './sms-shell-page.html',
})
export class SmsShellPage {
  private readonly route = inject(ActivatedRoute);
  private readonly crmService = inject(CrmService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly activeTab = signal<SmsTab>('Overview');
  protected readonly errorMessage = signal<string | null>(null);

  // Overview
  protected readonly balance = signal(0);
  protected readonly recentSms = signal<SmsLogRowDto[]>([]);
  protected readonly overviewLoading = signal(true);
  protected readonly showSendForm = signal(false);

  // SMS History
  protected readonly historyRows = signal<SmsLogRowDto[]>([]);
  protected readonly historyLoading = signal(true);
  protected readonly historyPage = signal(1);
  protected readonly historyPageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly historyTotalCount = signal(0);

  // Templates
  protected readonly templateRows = signal<SmsTemplateRowDto[]>([]);
  protected readonly templatesLoading = signal(true);
  protected readonly templatePage = signal(1);
  protected readonly templatePageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly templateTotalCount = signal(0);
  protected readonly showTemplateForm = signal(false);
  protected readonly editingTemplateId = signal<string | null>(null);
  protected readonly templateSaving = signal(false);
  protected readonly templateForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    content: ['', Validators.required],
  });

  // Credit History
  protected readonly ledgerBalance = signal(0);
  protected readonly ledgerRows = signal<SmsCreditLedgerRowDto[]>([]);
  protected readonly ledgerLoading = signal(true);
  protected readonly ledgerPage = signal(1);
  protected readonly ledgerPageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly ledgerTotalCount = signal(0);
  protected readonly showAdjustForm = signal(false);
  protected readonly adjustSaving = signal(false);
  protected readonly adjustForm = this.fb.nonNullable.group({
    changeAmount: [0, Validators.required],
    reason: [''],
  });

  constructor() {
    this.loadOverview();
  }

  protected switchTab(tab: SmsTab): void {
    this.activeTab.set(tab);
    this.errorMessage.set(null);
    if (tab === 'History' && this.historyRows().length === 0) {
      this.loadHistory();
    } else if (tab === 'Templates' && this.templateRows().length === 0) {
      this.loadTemplates();
    } else if (tab === 'CreditHistory' && this.ledgerRows().length === 0) {
      this.loadLedger();
    }
  }

  protected onSmsSent(): void {
    this.showSendForm.set(false);
    this.loadOverview();
  }

  // --- Overview ---

  private loadOverview(): void {
    this.overviewLoading.set(true);
    this.crmService.listSmsCreditLedger(this.organizationId, 1, 1).subscribe({
      next: (result) => this.balance.set(result.balance),
    });
    this.crmService.listSmsHistory(this.organizationId, 1, 5).subscribe({
      next: (result) => {
        this.recentSms.set(result.rows);
        this.overviewLoading.set(false);
      },
      error: (err: unknown) => {
        this.overviewLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load SMS overview.');
      },
    });
  }

  // --- History ---

  protected onHistoryPageChange(page: number): void {
    this.historyPage.set(page);
    this.loadHistory();
  }

  protected onHistoryPageSizeChange(pageSize: number): void {
    this.historyPageSize.set(pageSize);
    this.historyPage.set(1);
    this.loadHistory();
  }

  private loadHistory(): void {
    this.historyLoading.set(true);
    this.crmService.listSmsHistory(this.organizationId, this.historyPage(), this.historyPageSize()).subscribe({
      next: (result) => {
        this.historyRows.set(result.rows);
        this.historyTotalCount.set(result.totalCount);
        this.historyLoading.set(false);
      },
      error: (err: unknown) => {
        this.historyLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load SMS history.');
      },
    });
  }

  // --- Templates ---

  protected onTemplatePageChange(page: number): void {
    this.templatePage.set(page);
    this.loadTemplates();
  }

  protected onTemplatePageSizeChange(pageSize: number): void {
    this.templatePageSize.set(pageSize);
    this.templatePage.set(1);
    this.loadTemplates();
  }

  protected startCreateTemplate(): void {
    this.editingTemplateId.set(null);
    this.templateForm.reset({ title: '', content: '' });
    this.showTemplateForm.set(true);
  }

  protected startEditTemplate(row: SmsTemplateRowDto): void {
    this.editingTemplateId.set(row.id);
    this.templateForm.reset({ title: row.title, content: row.content });
    this.showTemplateForm.set(true);
  }

  protected cancelTemplateForm(): void {
    this.showTemplateForm.set(false);
    this.editingTemplateId.set(null);
  }

  protected saveTemplate(): void {
    if (this.templateForm.invalid) {
      this.templateForm.markAllAsTouched();
      return;
    }
    this.templateSaving.set(true);
    this.errorMessage.set(null);
    const { title, content } = this.templateForm.getRawValue();
    const editingId = this.editingTemplateId();

    const request$ = editingId
      ? this.crmService.updateSmsTemplate(this.organizationId, editingId, { title, content })
      : this.crmService.createSmsTemplate(this.organizationId, { title, content });

    request$.subscribe({
      next: () => {
        this.templateSaving.set(false);
        this.showTemplateForm.set(false);
        this.editingTemplateId.set(null);
        this.loadTemplates();
      },
      error: (err: unknown) => {
        this.templateSaving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save template. Please try again.');
      },
    });
  }

  protected deleteTemplate(row: SmsTemplateRowDto): void {
    if (!window.confirm(`Delete the "${row.title}" template? This cannot be undone.`)) {
      return;
    }
    this.crmService.deleteSmsTemplate(this.organizationId, row.id).subscribe({
      next: () => this.loadTemplates(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete template. Please try again.');
      },
    });
  }

  private loadTemplates(): void {
    this.templatesLoading.set(true);
    this.crmService.listSmsTemplates(this.organizationId, this.templatePage(), this.templatePageSize()).subscribe({
      next: (result) => {
        this.templateRows.set(result.rows);
        this.templateTotalCount.set(result.totalCount);
        this.templatesLoading.set(false);
      },
      error: (err: unknown) => {
        this.templatesLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load templates.');
      },
    });
  }

  // --- Credit History ---

  protected onLedgerPageChange(page: number): void {
    this.ledgerPage.set(page);
    this.loadLedger();
  }

  protected onLedgerPageSizeChange(pageSize: number): void {
    this.ledgerPageSize.set(pageSize);
    this.ledgerPage.set(1);
    this.loadLedger();
  }

  protected saveAdjustment(): void {
    if (this.adjustForm.invalid) {
      this.adjustForm.markAllAsTouched();
      return;
    }
    this.adjustSaving.set(true);
    this.errorMessage.set(null);
    const { changeAmount, reason } = this.adjustForm.getRawValue();

    this.crmService.adjustSmsCredit(this.organizationId, { changeAmount, reason: reason || null }).subscribe({
      next: () => {
        this.adjustSaving.set(false);
        this.showAdjustForm.set(false);
        this.adjustForm.reset({ changeAmount: 0, reason: '' });
        this.loadLedger();
        this.loadOverview();
      },
      error: (err: unknown) => {
        this.adjustSaving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not adjust SMS credit. Please try again.');
      },
    });
  }

  private loadLedger(): void {
    this.ledgerLoading.set(true);
    this.crmService.listSmsCreditLedger(this.organizationId, this.ledgerPage(), this.ledgerPageSize()).subscribe({
      next: (result) => {
        this.ledgerBalance.set(result.balance);
        this.ledgerRows.set(result.rows);
        this.ledgerTotalCount.set(result.totalCount);
        this.ledgerLoading.set(false);
      },
      error: (err: unknown) => {
        this.ledgerLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the SMS credit ledger.');
      },
    });
  }
}
