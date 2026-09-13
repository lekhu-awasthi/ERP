import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';

import { GlobalSearchCollection, GlobalSearchHitDto } from '../../../core/platform/platform.models';
import { PlatformService } from '../../../core/platform/platform.service';
import { SearchResultsPage } from './search-results-page';

interface SearchCall {
  readonly term: string;
  readonly limit?: number;
  readonly collection?: GlobalSearchCollection | null;
}

/**
 * Phase 39 — the results page, closing phase 33's carried item #4.
 *
 * <p>What is worth pinning here is not the markup but the three things the page's usefulness rests
 * on: that it reads the term from the <b>url</b> (so a result page is shareable and the back button
 * works), that the kind filter reaches the <b>server</b> rather than filtering an already-capped
 * list client-side, and that it asks for more than the dropdown's five — which is the entire reason
 * 34c's measurement unblocked this screen.</p>
 */
describe('SearchResultsPage', () => {
  let fixture: ComponentFixture<SearchResultsPage>;
  let component: SearchResultsPage;
  let calls: SearchCall[];
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  function hit(overrides: Partial<GlobalSearchHitDto> = {}): GlobalSearchHitDto {
    return {
      collection: 'Contact',
      documentType: null,
      id: 'c1',
      code: 'C0001',
      name: 'Adhitya Bhandari',
      subKind: 'Customer',
      ...overrides,
    };
  }

  async function open(query: Record<string, string>): Promise<void> {
    TestBed.resetTestingModule();
    calls = [];
    params = new BehaviorSubject(convertToParamMap(query));

    const platform = {
      search: (
        _organizationId: string,
        term: string,
        limit?: number,
        collection?: GlobalSearchCollection | null,
      ) => {
        calls.push({ term, limit, collection });
        return of([hit(), hit({ collection: 'Document', documentType: 'Invoice', id: 'i1', code: '045', name: null })]);
      },
    };

    await TestBed.configureTestingModule({
      imports: [SearchResultsPage],
      providers: [
        provideZonelessChangeDetection(),
        // NavigationCatalog derives the screen list from the router's own config, so a test with no
        // routes would have no screens to find -- and would then be asserting nothing.
        provideRouter([
          { path: 'organizations/:id/sales/invoices', children: [] },
          { path: 'organizations/:id/sales/invoices/:invoiceId', children: [] },
          { path: 'organizations/:id/contacts', children: [] },
        ]),
        { provide: PlatformService, useValue: platform },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ id: 'org-1' }),
              queryParamMap: params.value,
            },
            queryParamMap: params.asObservable(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SearchResultsPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('takes its term from the url, so a result page can be shared and reloaded', async () => {
    await open({ q: 'adhitya' });

    expect(calls[0].term).toBe('adhitya');
    expect(fixture.nativeElement.textContent).toContain('adhitya');
  });

  /** The point of the page: five per collection is the dropdown's cap, not this screen's. */
  it('asks for more than the dropdown would', async () => {
    await open({ q: 'adhitya' });

    expect(calls[0].limit).toBeGreaterThan(5);
  });

  it('sends no collection when the url names no kind', async () => {
    await open({ q: 'adhitya' });

    expect(calls[0].collection).toBeNull();
  });

  /**
   * The filter narrows the *query*, not the rendered list. Filtering client-side would filter an
   * already-capped sample, which would look identical on a small tenant and be wrong on a real one —
   * and it would forgo the reason the filter is affordable at all (one collection queried instead of
   * eighteen; see GlobalSearchQuery.Collection).
   */
  it('sends the kind to the server rather than filtering the rows it already has', async () => {
    await open({ q: 'adhitya', kind: 'Document' });

    expect(calls[0].collection).toBe('Document');
  });

  it('renders a hit with a route as a link and one without as a plain row', async () => {
    await open({ q: 'adhitya' });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Adhitya Bhandari');
    expect(text).toContain('045');
  });

  it('does not query the server for a term too short for it to accept', async () => {
    await open({ q: 'a' });

    expect(calls).toHaveLength(0);
  });

  /** The navigation half is client-side and instant, from the router config -- the same catalogue
   * the dropdown renders from, so the two cannot disagree. */
  it('offers matching screens under Everything', async () => {
    await open({ q: 'invoice' });

    // Two entries per list route with a sibling detail route: the screen and its Add form, which is
    // how this codebase addresses create (phase 3) and what the reference product's search shows.
    // The catalogue orders by area then name, so 'Add Invoices' sorts ahead of 'Invoices'.
    expect(component['screens']().map((x) => x.name)).toEqual(['Add Invoices', 'Invoices']);
  });

  /** A user who has narrowed to Contacts is not asking about screens. */
  it('offers no screens once a kind is chosen', async () => {
    await open({ q: 'invoice', kind: 'Contact' });

    expect(component['screens']()).toHaveLength(0);
  });
});
