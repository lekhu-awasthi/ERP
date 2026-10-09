import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PagedResult } from '../../core/common/paged-result';
import { Contact } from '../../core/contacts/contacts.models';
import { ContactsService } from '../../core/contacts/contacts.service';
import { PosCustomerSearch } from './pos-customer-search';

/** Phase 72 -- the one customer search the Retail till, the order screen and the bill screen share. */
describe('PosCustomerSearch', () => {
  const acme = { id: 'acme', code: 'C-0001', name: 'Acme Retail' } as Contact;
  const walkIn = { id: 'walk-in', code: 'WALKIN', name: 'Cash Customer' } as Contact;

  function mount(answer: () => Observable<PagedResult<Contact>>) {
    vi.useFakeTimers();
    const searches: (string | undefined)[] = [];
    TestBed.configureTestingModule({
      imports: [PosCustomerSearch],
      providers: [
        {
          provide: ContactsService,
          useValue: {
            listContacts: (_o: string, _t: string, _p: number, _s: number, filters?: { search?: string }) => {
              searches.push(filters?.search);
              return answer();
            },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(PosCustomerSearch);
    fixture.componentRef.setInput('organizationId', 'org-1');
    fixture.componentRef.setInput('inputId', 'pos-test-search');
    fixture.componentRef.setInput('excludeId', 'walk-in');
    const chosen: Contact[] = [];
    const failed: string[] = [];
    fixture.componentInstance.chosen.subscribe((contact) => chosen.push(contact));
    fixture.componentInstance.failed.subscribe((message) => failed.push(message));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('#pos-test-search')!;
    const type = (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      fixture.detectChanges();
    };

    return { fixture, element, input, type, chosen, failed, searches };
  }

  afterEach(() => vi.useRealTimers());

  it('labels its box, searches after a pause, leaves out the walk-in and hands back the one chosen', () => {
    const t = mount(() => of({ items: [walkIn, acme], page: 1, pageSize: 8, totalCount: 2 }));

    expect(t.element.querySelector('label')!.getAttribute('for')).toBe('pos-test-search');
    t.type('Ac');

    expect(t.searches).toEqual(['Ac']);
    expect([...t.element.querySelectorAll('button')].map((b) => b.textContent!.trim())).toEqual(['Acme Retail (C-0001)']);

    t.element.querySelector('button')!.click();
    t.fixture.detectChanges();

    expect(t.chosen).toEqual([acme]);
    expect(t.element.querySelectorAll('button').length).toBe(0);
    expect(t.input.value).toBe('');
  });

  it('gives focus back to the control that opened it once a customer is chosen, not to the page', () => {
    const t = mount(() => of({ items: [acme], page: 1, pageSize: 8, totalCount: 1 }));
    const opener = document.createElement('button');
    opener.id = 'pos-test-opener';
    document.body.appendChild(opener);
    t.fixture.componentRef.setInput('returnFocusTo', 'pos-test-opener');

    t.type('Ac');
    t.element.querySelector<HTMLButtonElement>('button')!.focus();
    t.element.querySelector<HTMLButtonElement>('button')!.click();
    t.fixture.detectChanges();
    TestBed.tick();

    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it('searches nothing for an empty box, and says when a search fails', () => {
    const t = mount(() => throwError(() => new Error('The server is down.')));

    t.type('   ');
    expect(t.searches).toEqual([]);

    t.type('Ac');
    expect(t.failed.length).toBe(1);
  });
});
