import { Injectable, signal } from '@angular/core';

import { CreditNoteConversionTemplate, InvoiceConversionTemplate } from './sales.models';
import { DebitNoteConversionTemplate, PurchaseBillConversionTemplate } from '../purchasing/purchasing.models';

/**
 * architecture-spec.md §3.3's "Convert to X" flow needs to hand a server-computed pre-fill from
 * one detail page to another's 'new' route. No query-param precedent for a whole line array in
 * this codebase yet, and URL-encoding an arbitrary-length Lines[] is exactly the "client-side
 * URL-payload trick" the spec explicitly avoided replacing Tigg's original approach with -- a
 * simple in-memory signal store (read once, then cleared) is simpler than either. Reused for the
 * Purchase-side conversions (PurchaseOrder->PurchaseBill, PurchaseBill->DebitNote) added in Phase
 * 6 -- same mechanism, not a new pattern.
 */
@Injectable({ providedIn: 'root' })
export class PendingTemplateStore {
  private readonly invoiceTemplateSignal = signal<InvoiceConversionTemplate | null>(null);
  private readonly creditNoteTemplateSignal = signal<CreditNoteConversionTemplate | null>(null);
  private readonly purchaseBillTemplateSignal = signal<PurchaseBillConversionTemplate | null>(null);
  private readonly debitNoteTemplateSignal = signal<DebitNoteConversionTemplate | null>(null);

  setInvoiceTemplate(template: InvoiceConversionTemplate): void {
    this.invoiceTemplateSignal.set(template);
  }

  takeInvoiceTemplate(): InvoiceConversionTemplate | null {
    const template = this.invoiceTemplateSignal();
    this.invoiceTemplateSignal.set(null);
    return template;
  }

  setCreditNoteTemplate(template: CreditNoteConversionTemplate): void {
    this.creditNoteTemplateSignal.set(template);
  }

  takeCreditNoteTemplate(): CreditNoteConversionTemplate | null {
    const template = this.creditNoteTemplateSignal();
    this.creditNoteTemplateSignal.set(null);
    return template;
  }

  setPurchaseBillTemplate(template: PurchaseBillConversionTemplate): void {
    this.purchaseBillTemplateSignal.set(template);
  }

  takePurchaseBillTemplate(): PurchaseBillConversionTemplate | null {
    const template = this.purchaseBillTemplateSignal();
    this.purchaseBillTemplateSignal.set(null);
    return template;
  }

  setDebitNoteTemplate(template: DebitNoteConversionTemplate): void {
    this.debitNoteTemplateSignal.set(template);
  }

  takeDebitNoteTemplate(): DebitNoteConversionTemplate | null {
    const template = this.debitNoteTemplateSignal();
    this.debitNoteTemplateSignal.set(null);
    return template;
  }
}
