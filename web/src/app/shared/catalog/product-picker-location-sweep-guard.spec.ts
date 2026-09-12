/**
 * Phase 36's client-side sweep guard, a sibling of `report-location-sweep-guard.spec.ts` (35b) and
 * `sweep-guard.spec.ts` (23).
 *
 * <p>A Product can be restricted to particular billing locations, and a document's line picker must
 * offer only the products available at that document's own location -- which the server does, given
 * the location. The failure mode is silent and per-screen: a form that keeps calling
 * `listAllProducts` directly still renders, still saves, and simply offers products the branch it
 * is raised at cannot use. So the rule is asserted over every form rather than remembered.</p>
 *
 * <p>Its mirror matters as much: a screen with <b>no</b> location of its own (a report filter, the
 * Products grid) must keep asking for every product, because narrowing a report's product filter to
 * one location would silently drop rows the report is meant to show.</p>
 */
const sources = import.meta.glob('/src/app/features/**/*.ts', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const components = Object.entries(sources).filter(([path]) => !path.endsWith('.spec.ts'));

describe('Product pickers follow the document location', () => {
  it('reads a plausible number of non-empty component files', () => {
    // Phase 34a: a guard must assert its input is non-empty, not merely defined -- Vite hands back
    // an empty string often enough that `toBeDefined()` passes over nothing at all.
    expect(components.length).toBeGreaterThan(100);
    expect(components.every(([, source]) => source.length > 0)).toBe(true);
  });

  it('finds the document forms it is meant to guard', () => {
    const documentForms = components.filter(([, source]) => source.includes('DocumentLocationPicker'));

    expect(documentForms.length).toBeGreaterThanOrEqual(11);
  });

  it('never calls listAllProducts directly from a form that carries a location', () => {
    const offenders = components
      .filter(([, source]) => source.includes('DocumentLocationPicker') && source.includes('listAllProducts('))
      .map(([path]) => path);

    // Use locationAwareProducts(...) instead: it passes the document's location and re-reads when
    // the header changes.
    expect(offenders).toEqual([]);
  });

  it('has every location-bearing form with a product picker ask for its own location', () => {
    const missing = components
      .filter(([, source]) => source.includes('DocumentLocationPicker') && source.includes('products.set('))
      .filter(([, source]) => !source.includes('locationAwareProducts('))
      .map(([path]) => path);

    expect(missing).toEqual([]);
  });

  it('leaves screens with no location of their own reading every product', () => {
    // Report filters and the Products grid: no document, no location, no narrowing.
    const wrongly = components
      .filter(([, source]) => !source.includes('DocumentLocationPicker') && source.includes('locationAwareProducts('))
      .map(([path]) => path);

    expect(wrongly).toEqual([]);
  });
});
