import { Component, OnInit, computed, inject, input, model, signal } from '@angular/core';

import { BASE_CURRENCY_CODE, Currency } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';

/**
 * Phase 28 (FR-2.5) -- the "Currency" + "Exchange Rate To NPR*" pair that sits on every
 * transactional document's form, and on the Opening Balances row form under the labels Currency +
 * Conversion Rate.
 *
 * <p><b>Shape confirmed live</b> on the reference tenant's Invoice and Customer Payment forms
 * (2026-09-04): the Currency picker is populated from the tenant's own <i>active currency list</i>
 * (rendered "CODE — Name"), and the Exchange Rate input is a plain manual number that is
 * <b>disabled and pinned to 1 whenever the selected currency is the base one</b>. There is no
 * date-driven rate lookup anywhere: the rate is typed, stored on the document, and carried along
 * verbatim by the conversion flow's pre-fill snapshot.</p>
 *
 * <p>That last behaviour is why a single-currency tenant needs no feature gate on any document
 * form: with one entry in the list, this control degenerates to "NPR, rate 1, read-only" all by
 * itself. See CreateCurrencyCommandHandler for the server half of the same argument.</p>
 *
 * <p>Two-way <code>model()</code>s, following <code>TermsEditor</code>: the host page owns both
 * values and sends them with the document's own save.</p>
 */
@Component({
  selector: 'app-currency-rate-fields',
  imports: [],
  templateUrl: './currency-rate-fields.html',
})
export class CurrencyRateFields implements OnInit {
  private readonly organizationsService = inject(OrganizationsService);

  readonly organizationId = input.required<string>();
  readonly disabled = input(false);

  /** The label the rate carries. "Exchange Rate To NPR" on documents, "Conversion Rate" on the
   * Opening Balances row -- both live-confirmed, same control. */
  readonly rateLabel = input(`Exchange Rate To ${BASE_CURRENCY_CODE}`);

  readonly currencyCode = model<string>(BASE_CURRENCY_CODE);
  readonly exchangeRate = model<number>(1);

  protected readonly baseCurrencyCode = BASE_CURRENCY_CODE;
  protected readonly currencies = signal<Currency[]>([]);

  /** Inactive currencies stay off the picker but are still shown when a document already carries
   * one -- a document raised in a currency the tenant later retired must keep displaying it. */
  protected readonly options = computed(() => {
    const active = this.currencies().filter((x) => x.isActive);
    const selected = this.currencyCode();
    return active.some((x) => x.code === selected)
      ? active
      : [...active, ...this.currencies().filter((x) => x.code === selected)];
  });

  protected readonly isBaseCurrency = computed(() => this.currencyCode() === BASE_CURRENCY_CODE);

  ngOnInit(): void {
    this.organizationsService.listCurrencies(this.organizationId()).subscribe({
      next: (items) => this.currencies.set(items),
      // A tenant whose list cannot be read (no permission, or the request failed) still gets a
      // working base-currency form rather than a broken one.
      error: () => this.currencies.set([]),
    });
  }

  protected onCurrencyChange(code: string): void {
    this.currencyCode.set(code);

    // The invariant the aggregate enforces (ExchangeRates.Validate) and the live form expresses by
    // disabling the input: a base-currency document's rate is exactly 1.
    if (code === BASE_CURRENCY_CODE) {
      this.exchangeRate.set(1);
    }
  }

  protected onRateChange(event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.exchangeRate.set(Number.isFinite(value) && value > 0 ? value : 1);
  }
}
