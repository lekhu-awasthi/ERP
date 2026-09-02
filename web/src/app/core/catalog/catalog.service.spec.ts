import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CatalogService } from './catalog.service';

/**
 * Phase 24, <b>Decision D -- proving the client-side sweep is complete, mechanically.</b>
 *
 * Every product picker and report filter in this app (fifteen of them, across Sales, Purchasing,
 * Inventory and Reports) gets its list from `CatalogService.listAllProducts`. That single seam is
 * the whole client-side sweep: defaulting it to `Transactable` makes all fifteen variant-aware at
 * once -- variant children appear as ordinary entries, variant parents disappear.
 *
 * Two things can silently undo that, and there is a test for each:
 * <ol>
 *   <li>Someone changes the default back to 'All', and every picker starts offering parents that
 *       can never be invoiced. The first test pins the wire format.</li>
 *   <li>Someone adds a sixteenth picker that calls `listProducts` directly, or `HttpClient`
 *       directly, bypassing the seam entirely. The guard test below reads the feature sources off
 *       disk and fails the build on that -- mirroring phase-23's sweep-guard.spec.ts.</li>
 * </ol>
 */
describe('CatalogService product listing (Phase 24 sweep)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  let service: CatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CatalogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for Transactable products by default, so no picker can offer a variant parent', () => {
    service.listAllProducts(organizationId).subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/products'));
    expect(request.request.params.get('variantFilter')).toBe('Transactable');
    request.flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
  });

  it('leaves the paginated products list unfiltered by default, matching the live Products screen', () => {
    service.listProducts(organizationId).subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/products'));
    expect(request.request.params.get('variantFilter')).toBeNull();
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it('passes an explicit filter through when one is given', () => {
    service.listProducts(organizationId, undefined, 1, 50, 'VariantParents').subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/products'));
    expect(request.request.params.get('variantFilter')).toBe('VariantParents');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });
});

describe('Phase 24 picker sweep completeness', () => {
  const sources = import.meta.glob('/src/app/features/**/*.ts', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  /**
   * Feature files allowed to call `listProducts` directly instead of going through
   * `listAllProducts`, each with the reason it is exempt.
   */
  const DIRECT_LIST_ALLOWED: ReadonlyMap<string, string> = new Map([
    [
      '/src/app/features/catalog/product-list-page/product-list-page.ts',
      'The Products screen itself -- it is paginated and deliberately shows all three roles, ' +
        'with its own user-facing variant filter.',
    ],
  ]);

  it('finds the feature sources to scan at all (guards against a glob matching nothing)', () => {
    // Without this, a broken glob makes every assertion below pass vacuously.
    expect(Object.keys(sources).length).toBeGreaterThan(50);
  });

  it('has no picker calling listProducts directly instead of the shared listAllProducts seam', () => {
    const found = Object.entries(sources)
      .filter(([path]) => !DIRECT_LIST_ALLOWED.has(path))
      .filter(([, source]) => /\.listProducts\(/.test(source))
      .map(([path]) => path)
      .sort();

    expect(
      found,
      `Product pickers must call catalogService.listAllProducts (which excludes variant parents) ` +
        `rather than listProducts:\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it('has no feature fetching /products over HttpClient directly', () => {
    const found = Object.entries(sources)
      .filter(([, source]) => /http\.get<[^>]*>\([^)]*['"`][^'"`]*\/products/.test(source))
      .map(([path]) => path)
      .sort();

    expect(
      found,
      `Go through CatalogService so the variant filter applies:\n  ${found.join('\n  ')}`,
    ).toEqual([]);
  });

  it('every exemption still points at a real file', () => {
    const stale = [...DIRECT_LIST_ALLOWED.keys()].filter((path) => !(path in sources));

    expect(stale, `Stale allow-list entries:\n  ${stale.join('\n  ')}`).toEqual([]);
  });
});
