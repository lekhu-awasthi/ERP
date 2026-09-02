import { Pipe, PipeTransform } from '@angular/core';

/**
 * NFR-1.2: Nepali/Indian lakh-crore digit grouping for every monetary figure on screen.
 *
 * <b>Why this pipe exists at all (Phase 23, Decision D).</b> Before this phase money was rendered
 * with a bare `.toFixed(2)` inline in templates -- 324 of them across 40 files, with no pipe in the
 * app and no shared formatting anywhere. `.toFixed(2)` groups nothing, so every amount in the
 * product read in plain digits. This is the single replacement for all of them, and
 * `no-inline-money-format.spec.ts` fails the build if a new one appears.
 *
 * <b>The contract, stated once because 324 call sites now depend on it:</b>
 *   - Grouping is `en-IN`, which is the lakh/crore convention: 1,00,000.00 / 10,00,000.00 /
 *     1,00,00,000.00. A value below 100,000 groups identically under both conventions, which is why
 *     the spec asserts values above it.
 *   - Always exactly 2 decimal places, matching the `.toFixed(2)` it replaces.
 *   - Rounding is `Intl`'s default half-expand (half away from zero). `.toFixed(2)` nominally does
 *     the same but is subject to binary-float artifacts -- `(1.005).toFixed(2)` is "1.00" while this
 *     pipe gives "1.01". That is a deliberate, small, strictly-more-correct behaviour change, taken
 *     knowingly rather than discovered: see the status doc.
 *   - null/undefined render as the empty string rather than throwing, so a nullable DTO field can
 *     be piped directly. Every existing call site passed a non-null number.
 *
 * `Intl.NumberFormat` is native ECMA-402, so no `registerLocaleData` and no locale bundle is
 * needed -- unlike Angular's own `DecimalPipe`, which would have required registering `en-IN`.
 *
 * Note the live reference product is itself inconsistent here (its Journal Voucher grid groups as
 * lakh/crore, its Home dashboard's balance panel groups Western). NFR-1.2 says "wherever
 * displayed", so this app follows the PRD and groups lakh/crore everywhere.
 */
@Pipe({ name: 'amount' })
export class AmountPipe implements PipeTransform {
  private static readonly formatter = new Intl.NumberFormat('en-IN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

  transform(value: number | string | null | undefined): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }
    const numeric = typeof value === 'number' ? value : Number(value);
    if (!Number.isFinite(numeric)) {
      return '';
    }
    // Any tiny negative (and -0 itself) formats as "-0.00", which reads as a real negative to an
    // accountant. Normalised on the formatted string rather than by pre-rounding the input, since
    // pre-rounding would quietly change the half-away-from-zero contract documented above.
    const text = AmountPipe.formatter.format(numeric);
    return text === '-0.00' ? '0.00' : text;
  }
}
