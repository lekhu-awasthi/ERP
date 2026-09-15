import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import {
  CreateCustomStatusRequest,
  CustomStatus,
  UpdateCustomStatusRequest,
} from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { CustomStatusListPage } from './custom-status-list-page';

/**
 * The editor phase 20b never built. Its picker shipped on four list grids with no way to define an
 * option, so the dropdown offered nothing on every tenant.
 *
 * <p>What these assert is mostly the <b>grouping</b>, because that is the part a flat list would get
 * wrong invisibly: a status belongs to exactly one document type and appears only in that type's
 * grid, so a Quotation status shown under Sales Order would be a status the user can never reach.
 * The empty-state text is asserted too — it is what tells someone looking at a bare "Select Status"
 * dropdown why it is bare.</p>
 */
describe('CustomStatusListPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function status(over: Partial<CustomStatus> = {}): CustomStatus {
    return {
      id: 'cs-1',
      organizationId,
      name: 'Accepted',
      documentType: 'Quotation',
      isActive: true,
      createdAt: '2026-09-15T00:00:00Z',
      ...over,
    };
  }

  let fixture: ComponentFixture<CustomStatusListPage>;
  let created: CreateCustomStatusRequest[];
  let updated: { id: string; request: UpdateCustomStatusRequest }[];
  let deleted: string[];

  async function render(items: CustomStatus[]): Promise<void> {
    created = [];
    updated = [];
    deleted = [];

    const configuration: Partial<ConfigurationService> = {
      listCustomStatuses: (): Observable<CustomStatus[]> => of(items),
      createCustomStatus: (_o: string, request: CreateCustomStatusRequest) => {
        created.push(request);
        return of(status(request as Partial<CustomStatus>));
      },
      updateCustomStatus: (_o: string, id: string, request: UpdateCustomStatusRequest) => {
        updated.push({ id, request });
        return of(status({ id, ...request }));
      },
      deleteCustomStatus: (_o: string, id: string): Observable<void> => {
        deleted.push(id);
        return of(undefined as void);
      },
    };

    await TestBed.configureTestingModule({
      imports: [CustomStatusListPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ConfigurationService, useValue: configuration },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', organizationId]]) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomStatusListPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function sectionTitles(): string[] {
    return Array.from(el().querySelectorAll('h6')).map((h) => h.textContent?.trim() ?? '');
  }

  it('offers only the four document types whose list grid renders a picker', async () => {
    await render([]);

    const options = Array.from(
      el().querySelectorAll<HTMLOptionElement>('#custom-status-list-page-document-type option'),
    ).map((o) => o.textContent?.trim());

    expect(options).toEqual(['Quotation', 'Sales Order', 'Purchase Order', 'Production Order']);
  });

  it('groups each status under the document type it is scoped to', async () => {
    await render([
      status({ id: 'cs-1', name: 'Accepted', documentType: 'Quotation' }),
      status({ id: 'cs-2', name: 'Confirmed', documentType: 'PurchaseOrder' }),
    ]);

    const quotation = el().querySelectorAll('.card')[3];
    expect(sectionTitles()).toContain('Quotation');
    expect(sectionTitles()).toContain('Purchase Order');
    expect(quotation.textContent).toContain('Accepted');
    expect(quotation.textContent).not.toContain('Confirmed');
  });

  it('explains an empty section rather than leaving it blank', async () => {
    await render([]);

    // This is the sentence that answers "why does my Select Status dropdown have nothing in it".
    expect(el().textContent).toContain('No statuses for Quotation yet');
    expect(el().textContent).toContain('Select Status');
  });

  it('marks an inactive status, which the picker filters out', async () => {
    await render([status({ isActive: false })]);

    expect(el().textContent).toContain('Inactive');
  });

  it('creates a status without sending isActive, which a new one always is', async () => {
    await render([]);

    const name = el().querySelector<HTMLInputElement>('#custom-status-list-page-name')!;
    name.value = 'Follow Up';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    el().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(created).toEqual([{ name: 'Follow Up', documentType: 'Quotation' }]);
    expect(updated).toEqual([]);
  });

  it('edits through the update call, carrying the active flag', async () => {
    await render([status({ id: 'cs-7', name: 'Accepted', documentType: 'Quotation' })]);

    el().querySelector<HTMLButtonElement>('button[aria-label="Edit the Accepted status"]')!.click();
    fixture.detectChanges();

    el().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(updated).toEqual([
      { id: 'cs-7', request: { name: 'Accepted', documentType: 'Quotation', isActive: true } },
    ]);
    expect(created).toEqual([]);
  });

  it('asks before deleting, and deletes only on confirm', async () => {
    await render([status({ id: 'cs-9', name: 'Rejected' })]);

    el().querySelector<HTMLButtonElement>('button[aria-label="Delete the Rejected status"]')!.click();
    fixture.detectChanges();

    expect(el().textContent).toContain('Delete this status?');
    expect(deleted).toEqual([]);

    Array.from(el().querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Confirm')!
      .click();
    fixture.detectChanges();

    expect(deleted).toEqual(['cs-9']);
  });
});
