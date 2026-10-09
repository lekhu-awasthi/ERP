/**
 * Phase 70 -- the client's half of `PackagingFeeSweepGuardTests`. A till line's money has four parts
 * (amount, service charge, packaging fee, VAT), and a screen that adds them up itself -- the ERP invoice and
 * credit-note pages fold a till sale's taxable bucket in the browser -- must add all four. The compiler
 * cannot see a sum that leaves one out: it type-checks, and the page shows a taxable total short by every
 * parcel's fee. Phase 70 found exactly that on both detail pages.
 *
 * <p>So the rule is asserted over every source and template: wherever a line's service charge is added to
 * its amount, its packaging fee is added too; and any template that prints a service-charge figure also
 * prints the packaging fee beside it.</p>
 */
const sources = import.meta.glob('/src/app/**/*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const templates = import.meta.glob('/src/app/**/*.html', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const SUM_WITHOUT_FEE = /\b(\w+)\.amount \+ \1\.serviceChargeAmount\b(?! \+ \1\.packagingFeeAmount)/g;

/** A template printing a service-charge figure: `x.serviceCharge`, `x.serviceChargeTotal`, `x.totalServiceCharge`. */
const PRINTS_SERVICE_CHARGE = /\b\w+\.(?:serviceCharge|serviceChargeTotal|totalServiceCharge)\b(?!Rate|Enabled|Applicable|OnTakeAway|AccountId)/;
const PRINTS_PACKAGING_FEE = /\b\w+\.(?:packagingFee|packagingFeeTotal|totalPackagingFee)\b/;

/**
 * Templates that name a service-charge figure for a reason other than printing a bill's or a report's
 * money, each with why the fee has no place beside it.
 */
const exempt: Readonly<Record<string, string>> = {
  // The settings screen edits the service-charge *setting*; its packaging-fee setting is its own fieldset.
  'pos-settings-page.html': 'a settings form, not a figure',
};

describe('the packaging-fee sweep on the client', () => {
  it('reads every source and template it guards', () => {
    // Phase 34a: assert the input is non-empty, not merely defined.
    expect(Object.keys(sources).length).toBeGreaterThan(300);
    expect(Object.keys(templates).length).toBeGreaterThan(100);
    expect(Object.values(sources).every((x) => typeof x === 'string')).toBe(true);
  });

  it('never adds a line\'s service charge to its amount without its packaging fee', () => {
    const offenders = Object.entries(sources)
      .filter(([path]) => !path.endsWith('packaging-fee-sweep-guard.spec.ts'))
      .flatMap(([path, source]) => [...source.matchAll(SUM_WITHOUT_FEE)].map((m) => `${path}: ${m[0]}`));

    expect(offenders).toEqual([]);
  });

  it('prints the packaging fee wherever a template prints a service-charge figure', () => {
    const offenders = Object.entries(templates)
      .filter(([path]) => !Object.keys(exempt).some((name) => path.endsWith(`/${name}`)))
      .filter(([, template]) => PRINTS_SERVICE_CHARGE.test(template) && !PRINTS_PACKAGING_FEE.test(template))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });

  it('still finds the screens it was written for, and each exemption still names a real template', () => {
    // Non-vacuity in both directions (phase 46): the patterns match the pages that print both, and an
    // exemption whose template is gone is stale.
    const printing = Object.entries(templates)
      .filter(([, template]) => PRINTS_SERVICE_CHARGE.test(template))
      .map(([path]) => path);
    expect(printing.some((path) => path.endsWith('/invoice-detail-page.html'))).toBe(true);
    expect(printing.some((path) => path.endsWith('/pos-till-page.html'))).toBe(true);
    expect(printing.length).toBeGreaterThanOrEqual(10);

    for (const name of Object.keys(exempt)) {
      expect(Object.keys(templates).some((path) => path.endsWith(`/${name}`))).toBe(true);
    }
  });

  it('tells a missing fee from a present one', () => {
    expect('l.amount + l.serviceChargeAmount, 0'.match(SUM_WITHOUT_FEE)).not.toBeNull();
    expect('l.amount + l.serviceChargeAmount + l.packagingFeeAmount, 0'.match(SUM_WITHOUT_FEE)).toBeNull();
    expect(PRINTS_SERVICE_CHARGE.test('{{ p.serviceCharge | amount }}')).toBe(true);
    expect(PRINTS_SERVICE_CHARGE.test('{{ line.serviceChargeRate }}%')).toBe(false);
  });
});
