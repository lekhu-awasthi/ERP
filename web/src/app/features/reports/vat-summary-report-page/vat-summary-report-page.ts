import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { VatReturnLineDto, VatReturnSection, VatSummaryReportDto } from '../../../core/accounting/accounting.models';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { currentFiscalYear, fiscalYearLabel, supportedFiscalYears } from '../../../shared/formatting/bs-fiscal-year';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/** The heading each block of the return prints above its lines. */
const SECTION_TITLES: Readonly<Record<VatReturnSection, string>> = {
  Sales: 'Sales',
  SalesReturns: 'Sales returns',
  Purchases: 'Purchases',
  PurchaseReturns: 'Purchase returns',
  Settlement: 'VAT for the month',
};

/**
 * Phase 89 -- the VAT return, a Bikram Sambat fiscal year at a time.
 *
 * <p>Read live first (2026-10-12): the vendor's VAT Summary is the IRD return, a fiscal-year picker and twelve BS month
 * columns, Shrawan to Asar, one row per line of the form. Phase 8c's screen (three VAT-rate buckets over any date
 * range) was designed before anyone had opened the vendor's, and a date range can straddle two VAT periods. Each line
 * shows its English label with the form's Nepali beside it (the user's choice). The figures come from the readers the
 * Sales and Purchase Registers read, so a month here equals the registers over that month.</p>
 */
@Component({
  selector: 'app-vat-summary-report-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, StatusBanner],
  templateUrl: './vat-summary-report-page.html',
})
export class VatSummaryReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly fiscalYears = supportedFiscalYears();
  protected readonly fiscalYearLabel = fiscalYearLabel;
  protected readonly fiscalYear = signal(currentFiscalYear());

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<VatSummaryReportDto | null>(null);
  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFiscalYearChange(event: Event): void {
    this.fiscalYear.set(Number((event.target as HTMLSelectElement).value));
    this.load();
  }

  /** The return's lines grouped into its blocks, in the form's order. */
  protected readonly sections = computed(() => {
    const blocks: { section: VatReturnSection; title: string; lines: VatReturnLineDto[] }[] = [];
    for (const line of this.report()?.lines ?? []) {
      const last = blocks[blocks.length - 1];
      if (last?.section === line.section) {
        last.lines.push(line);
      } else {
        blocks.push({ section: line.section, title: SECTION_TITLES[line.section], lines: [line] });
      }
    }
    return blocks;
  });

  protected exportReport(): void {
    const fiscalYear = this.fiscalYear();
    this.exporting.set(true);
    this.accountingService.exportVatSummaryReport(this.organizationId, fiscalYear).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `VatSummaryReport_BS${fiscalYear}-${fiscalYear + 1}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the VAT Summary Report.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.accountingService.getVatSummaryReport(this.organizationId, this.fiscalYear()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the VAT Summary Report.');
      },
    });
  }
}
