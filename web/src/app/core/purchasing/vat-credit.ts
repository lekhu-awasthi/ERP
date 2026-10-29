/**
 * Phase 99 -- a purchase line's input-credit terms (VAT Rules 40 and 41), and the words the forms show for them. One list
 * for the bill, the expense and the product form, so a label cannot differ between them.
 */

/** What the purchase was used for (Rule 40). Taxable is the default and what every line before phase 99 was. */
export type VatCreditUse = 'Taxable' | 'Exempt' | 'Shared';

/** Rule 41's limit on the credit, whatever the business sells. Full is the default. */
export type VatCreditCategory = 'Full' | 'Blocked' | 'Vehicle' | 'Computer';

export const VAT_CREDIT_USES: readonly { value: VatCreditUse; label: string }[] = [
  { value: 'Taxable', label: 'Taxable use' },
  { value: 'Exempt', label: 'Exempt use' },
  { value: 'Shared', label: 'Shared' },
];

export const VAT_CREDIT_CATEGORIES: readonly { value: VatCreditCategory; label: string }[] = [
  { value: 'Full', label: 'Full credit' },
  { value: 'Blocked', label: 'No credit (Rule 41)' },
  { value: 'Vehicle', label: 'Vehicle, 40%' },
  { value: 'Computer', label: 'Computer, 60%' },
];

export function vatCreditUseLabel(use: VatCreditUse | null | undefined): string {
  return VAT_CREDIT_USES.find((x) => x.value === use)?.label ?? 'Taxable use';
}

export function vatCreditCategoryLabel(category: VatCreditCategory | null | undefined): string {
  return VAT_CREDIT_CATEGORIES.find((x) => x.value === category)?.label ?? 'Full credit';
}
