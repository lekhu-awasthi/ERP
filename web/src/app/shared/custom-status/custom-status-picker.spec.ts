import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';

import { ConfigurationService } from '../../core/configuration/configuration.service';
import { CustomStatusPicker } from './custom-status-picker';

/**
 * The picker used to render **inside** each list row's `<a [routerLink]>`, and carried two opposite
 * event handlers to survive that: a click that stopped propagation *and* prevented the default
 * (stopping propagation alone suppressed RouterLink's own listener, the one that calls
 * preventDefault, so native anchor navigation opened the document), and a mousedown that did
 * neither, because a native `<select>` opens its popup as the default action of mousedown.
 *
 * <p><b>Phase 47 removed the nesting instead.</b> A control inside a link is invalid HTML that no
 * handler makes valid — it lands in the link's accessible name, and a keyboard user who tabs onto
 * it is standing inside a link. The four rows are now a `<div>` with a `stretched-link` on the
 * document code, so this control is a sibling of the link. The two navigation assertions are gone
 * with the handlers they pinned; what replaces them is stronger and lives in
 * `a11y-sweep-guard.spec.ts`: <i>no</i> template nests an interactive control in a link or a
 * button, asserted over every template in the app rather than over this one component.</p>
 *
 * <p>The assertion kept here is the one about this component's own job — what it saves — plus the
 * new one that it no longer swallows a click, so a later "restore the guard" edit has to argue with
 * a test rather than with a comment.</p>
 */
describe('CustomStatusPicker — a sibling of the row link, not a child of it', () => {
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

  it('no longer swallows the click, because there is no anchor left to swallow it from', async () => {
    await render();

    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    select().dispatchEvent(event);

    // Phase 45 had to prevent this; phase 47 made it unnecessary by taking the control out of the
    // link. Asserted rather than assumed, so the workaround cannot quietly come back with the
    // nesting it was for.
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
