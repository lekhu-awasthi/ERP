import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';

import { ConfigurationService } from '../../core/configuration/configuration.service';
import { CustomStatusPicker } from './custom-status-picker';

/**
 * The picker renders **inside** each list row's `<a [routerLink]>`, which is why these two
 * assertions exist and why they are opposites.
 *
 * <p><b>The bug they pin.</b> Clicking Select Status on the Quotation list opened the quotation
 * instead of the dropdown. The control already called `stopPropagation()`, and that was not merely
 * insufficient — it was the cause. Stopping propagation prevents RouterLink's own click listener
 * from running, and RouterLink's listener is the thing that calls `preventDefault()` before
 * navigating in-app. Suppressing it left the browser's *native* anchor navigation as the only
 * behaviour, so the row loaded. The guard has to prevent the default as well.</p>
 *
 * <p><b>And mousedown must NOT prevent the default</b>, which is the half a later "consistency"
 * edit would most plausibly break: a native `<select>` opens its popup as the default action of
 * mousedown, so preventing it there stops the dropdown opening at all. Both directions are asserted
 * so neither can be tidied into the other.</p>
 */
describe('CustomStatusPicker — nested inside a row anchor', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const documentId = '22222222-2222-2222-2222-222222222222';

  let fixture: ComponentFixture<CustomStatusPicker>;
  let saved: (string | null)[];

  async function render(customStatusId: string | null = null): Promise<void> {
    saved = [];

    const configuration: Partial<ConfigurationService> = {
      setCustomStatus: (_org: string, _type, _id: string, value: string | null): Observable<void> => {
        saved.push(value);
        return of(undefined as void);
      },
    };

    await TestBed.configureTestingModule({
      imports: [CustomStatusPicker],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigurationService, useValue: configuration },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomStatusPicker);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('documentType', 'Quotation');
    fixture.componentRef.setInput('documentId', documentId);
    fixture.componentRef.setInput('customStatusId', customStatusId);
    fixture.componentRef.setInput('options', [
      { id: 'cs-1', name: 'Accepted', documentType: 'Quotation', isActive: true },
      { id: 'cs-2', name: 'Rejected', documentType: 'Quotation', isActive: true },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function select(): HTMLSelectElement {
    return (fixture.nativeElement as HTMLElement).querySelector('select')!;
  }

  it('prevents the row anchor from navigating when the control is clicked', async () => {
    await render();

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    select().dispatchEvent(event);

    // Without this the browser follows the enclosing <a href> and the document opens.
    expect(event.defaultPrevented).toBe(true);
  });

  it('lets mousedown keep its default, or the dropdown would never open', async () => {
    await render();

    const event = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    select().dispatchEvent(event);

    expect(event.defaultPrevented).toBe(false);
  });

  it('still saves the chosen status', async () => {
    await render();

    const el = select();
    el.value = 'cs-2';
    el.dispatchEvent(new Event('change'));

    expect(saved).toEqual(['cs-2']);
  });

  it('sends null when the placeholder is chosen, rather than an empty string', async () => {
    await render('cs-1');

    const el = select();
    el.value = '';
    el.dispatchEvent(new Event('change'));

    expect(saved).toEqual([null]);
  });
});
