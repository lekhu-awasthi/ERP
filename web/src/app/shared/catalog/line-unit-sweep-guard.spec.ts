/**
 * Phase 54's client-side sweep guard, a sibling of `product-picker-location-sweep-guard.spec.ts`
 * (36), `report-location-sweep-guard.spec.ts` (35b) and `sweep-guard.spec.ts` (23).
 *
 * <p><b>Why this exists.</b> `unitId` is <i>optional</i> on every line-input record, by design: a
 * caller that does not send it means the product's own primary unit, which is what every line meant
 * before phase 52. That optionality is also what ended phase 52's sweep on the client side -- seven
 * of eight Angular forms rendered the control, compiled clean, passed every spec and silently
 * dropped the field on the way to the server, saving a carton line as pieces. Phase 51's lesson is
 * that a sweep driven by the compiler stops exactly where the compiler stops; in TypeScript an
 * optional property stops it immediately.</p>
 *
 * <p>So the rule is asserted over every form rather than remembered. It is asserted in <b>both</b>
 * directions (phase 30, phase 46): the forms whose document type carries a unit must render the
 * control <i>and</i> send the field, and the manufacturing forms -- whose live originals show the
 * product's primary unit as static text with nothing to choose -- must do neither. Without the
 * second half, a future phase could add the control to a Bill of Materials without anyone having to
 * argue for it.</p>
 */
const sources = import.meta.glob('/src/app/features/**/*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const templates = import.meta.glob('/src/app/features/**/*.html', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const components = Object.entries(sources).filter(([path]) => !path.endsWith('.spec.ts'));

/**
 * The nine document forms whose lines carry a unit: the eight from phase 52's 2026-09-17 read, plus
 * Inventory Adjustment from phase 54's 2026-09-20 row-present read. Named rather than derived, for
 * phase 50's reason -- a predicate naming a type stops covering anything that predates it.
 */
const unitBearingForms = [
  'quotation-detail-page',
  'sales-order-detail-page',
  'invoice-detail-page',
  'credit-note-detail-page',
  'purchase-order-detail-page',
  'purchase-bill-detail-page',
  'debit-note-detail-page',
  'warehouse-transfer-detail-page',
  'inventory-adjustment-detail-page',
];

/**
 * The manufacturing forms, whose live originals render the unit as a plain display element with no
 * select even for a product carrying a secondary unit -- read 2026-09-20 on the same tenant, with
 * the same product, in the same session as the Inventory Adjustment reading above. Opening Stock is
 * not listed: it has no line grid at all.
 */
const unitlessForms = [
  'bom-detail-page',
  'production-order-detail-page',
  'production-journal-detail-page',
];

function fileFor(name: string, files: [string, string][]): [string, string] | undefined {
  return files.find(([path]) => path.includes(`/${name}/${name}.`));
}

describe('The line unit control follows the nine document types that carry a unit', () => {
  it('reads a plausible number of non-empty component files and templates', () => {
    // Phase 34a: a guard must assert its input is non-empty, not merely defined -- Vite hands back
    // an empty string often enough that `toBeDefined()` passes over nothing at all.
    expect(components.length).toBeGreaterThan(100);
    expect(components.every(([, source]) => source.length > 0)).toBe(true);
    expect(Object.keys(templates).length).toBeGreaterThan(100);
  });

  it('finds every form it names, on both sides of the rule', () => {
    // Without this the two lists below could rot into names that match nothing, and every
    // assertion over them would pass vacuously (phase 34a).
    const missing = [...unitBearingForms, ...unitlessForms].filter(
      (name) => !fileFor(name, components),
    );

    expect(missing).toEqual([]);
  });

  it('has every unit-bearing form render the control in its Qty cell', () => {
    const missing = unitBearingForms.filter((name) => {
      const template = fileFor(name, Object.entries(templates));
      return !template || !template[1].includes('<app-line-unit');
    });

    expect(missing).toEqual([]);
  });

  it('has every unit-bearing form name the unit in its read-only branch too', () => {
    // Phase 52's scripted template sweep matched only the editable `<input>`, so seven approved
    // documents rendered a quantity with no unit at all -- a defect the browser pass caught and no
    // spec did. A form covers exactly the shape its pattern names.
    const missing = unitBearingForms.filter((name) => {
      const template = fileFor(name, Object.entries(templates));
      return !template || !template[1].includes('unitLabel(');
    });

    expect(missing).toEqual([]);
  });

  it('has every unit-bearing form actually send the unit to the server', () => {
    // The half that compiles either way, and therefore the half worth a guard: `unitId` is optional
    // on the request record, so a form that builds its line inputs without it saves a carton line
    // as pieces and nothing complains.
    const missing = unitBearingForms.filter((name) => {
      const component = fileFor(name, components);
      return !component || !/unitId:\s*l(ine)?\.unitId\s*\|\|\s*null/.test(component[1]);
    });

    expect(missing).toEqual([]);
  });

  it('leaves the manufacturing forms with no unit control and no unit on the wire', () => {
    const wrongly = unitlessForms.filter((name) => {
      const component = fileFor(name, components);
      const template = fileFor(name, Object.entries(templates));

      return (
        (component?.[1].includes('LineUnitControl') ?? false) ||
        (component?.[1].includes('unitId') ?? false) ||
        (template?.[1].includes('<app-line-unit') ?? false)
      );
    });

    // If this fails, the 2026-09-20 reading recorded in `UnitSweepGuardTests.UnitlessLineTypes`
    // has to be re-taken and the reason rewritten -- not the assertion relaxed.
    expect(wrongly).toEqual([]);
  });
});
