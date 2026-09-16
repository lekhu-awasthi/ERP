import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { CrmService } from '../../../core/crm/crm.service';
import { DealRow } from '../../../core/crm/crm.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { DealForm } from './deal-form';

/**
 * Phase 48 (43 carried item #4). See {@link TaskForm}'s spec for the finding; the Deal side adds one
 * rule of its own, which is that <b>Contact is not editable</b>. `UpdateDealCommand` carries no
 * `ContactId`, and phase 43's `WorkspaceName` rule says a field an aggregate refuses should be
 * absent rather than present-and-ignored — a select the user can change and the server discards
 * reads as accepted.
 */
describe('DealForm', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const dealId = '22222222-2222-2222-2222-222222222222';
  const contactId = '33333333-3333-3333-3333-333333333333';
  const sourceId = '44444444-4444-4444-4444-444444444444';
  const userId = '55555555-5555-5555-5555-555555555555';

  function deal(overrides: Partial<DealRow> = {}): DealRow {
    return {
      id: dealId,
      title: 'Cust Deal',
      contactId,
      contactName: 'Soniya Maharjan',
      leadSourceId: sourceId,
      leadSourceName: 'Referral',
      description: 'Renewal conversation',
      expectedRevenue: 1230,
      expectedClosingDate: '2026-08-29',
      stageId: null,
      stageName: 'Qualified',
      stageColor: null,
      status: 'Pending',
      isPrivate: false,
      closingDate: null,
      createdByUserId: userId,
      createdByName: 'Asha Sharma',
      createdAt: '2026-06-29T05:03:55.0000000+00:00',
      assignees: [{ userId, name: 'Asha Sharma' }],
      ...overrides,
    };
  }

  function mount(row: DealRow | null, impliedContactId: string | null = null) {
    const created: Record<string, unknown>[] = [];
    const updated: { id: string; request: Record<string, unknown> }[] = [];

    const crm = {
      createDeal: (_org: string, request: Record<string, unknown>): Observable<unknown> => {
        created.push(request);
        return of({ id: dealId });
      },
      updateDeal: (_org: string, id: string, request: Record<string, unknown>): Observable<void> => {
        updated.push({ id, request });
        return of(undefined);
      },
    };

    TestBed.configureTestingModule({
      imports: [DealForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CrmService, useValue: crm },
        {
          provide: ConfigurationService,
          useValue: {
            listLeadSources: () => of([{ id: sourceId, name: 'Referral' }]),
            listDealStages: () => of([]),
          },
        },
        {
          provide: ContactsService,
          useValue: { listAllContacts: () => of([{ id: contactId, name: 'Soniya Maharjan', type: 'Customer' }]) },
        },
        { provide: OrganizationsService, useValue: { listMembers: () => of([{ userId, fullName: 'Asha Sharma' }]) } },
      ],
    });

    const fixture = TestBed.createComponent(DealForm);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('deal', row);
    fixture.componentRef.setInput('contactId', impliedContactId);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const save = () => {
      element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
    };

    return { fixture, element, created, updated, save };
  }

  afterEach(() => TestBed.resetTestingModule());

  function value(element: HTMLElement, id: string): string {
    return element.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`#${id}`)!.value;
  }

  it('prefills every field the update command carries, including the assignees', () => {
    const { element } = mount(deal());

    expect(value(element, 'deal-form-title')).toBe('Cust Deal');
    expect(value(element, 'deal-form-description')).toBe('Renewal conversation');
    expect(value(element, 'deal-form-lead-source')).toBe(sourceId);
    expect(value(element, 'deal-form-expected-revenue')).toBe('1230');
    expect(element.querySelector<HTMLInputElement>(`#deal-form-assignee-${userId}`)!.checked).toBe(true);
  });

  it('shows the contact as text when editing, with no control to change it', () => {
    const { element } = mount(deal());

    expect(element.textContent).toContain('Soniya Maharjan');
    expect(
      element.querySelector('#deal-form-contact'),
      'the update command carries no ContactId, so there must be no control offering one',
    ).toBeNull();
  });

  it('offers a contact picker when creating without an implied contact', () => {
    const { element } = mount(null);

    expect(element.querySelector('#deal-form-contact')).toBeTruthy();
  });

  it('hides the picker when the host implies the contact', () => {
    // The Contact detail page's Deals tab: the contact is the page.
    const { element } = mount(null, contactId);

    expect(element.querySelector('#deal-form-contact')).toBeNull();
  });

  it('updates the record it holds, and never creates a second one', () => {
    const { created, updated, save } = mount(deal());

    save();

    expect(created.length, 'editing must not POST a duplicate').toBe(0);
    expect(updated.length).toBe(1);
    expect(updated[0].id).toBe(dealId);
    expect(updated[0].request).toMatchObject({
      title: 'Cust Deal',
      leadSourceId: sourceId,
      expectedRevenue: 1230,
      expectedClosingDate: '2026-08-29',
      isPrivate: false,
      assigneeUserIds: [userId],
    });
    expect(updated[0].request).not.toHaveProperty('contactId');
  });

  it('refuses to create a deal with no contact chosen', () => {
    const { element, created, save } = mount(null);

    const title = element.querySelector<HTMLInputElement>('#deal-form-title')!;
    title.value = 'A new deal';
    title.dispatchEvent(new Event('input'));

    save();

    expect(created.length).toBe(0);
  });
});
