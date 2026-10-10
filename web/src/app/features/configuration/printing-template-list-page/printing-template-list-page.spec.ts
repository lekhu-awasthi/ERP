import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PrintingTemplate, PrintingTemplateLocationChoice } from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { BillingLocation } from '../../../core/organizations/organizations.models';
import { BillingLocationStore } from '../../../shared/locations/billing-location-store';
import { PrintingTemplateListPage } from './printing-template-list-page';

/**
 * Phase 75 -- the page's "By location" card: a location may print a type with its own template. It is offered only
 * to an organization with more than one location, only for the types that carry a location there and have an active
 * template, and its "Organization default" option clears the choice rather than naming a template.
 */
describe('PrintingTemplateListPage, by location', () => {
  const organizationId = 'org-1';

  function template(id: string, name: string, over: Partial<PrintingTemplate> = {}): PrintingTemplate {
    return {
      id, organizationId, name, documentType: 'Invoice', isDefault: false, isActive: true, createdAt: '2026-10-10T00:00:00Z',
      layout: 'Standard', hiddenElements: [], printedCustomFieldIds: [], footerText: null, compactMargins: false, ...over,
    };
  }

  function location(id: string, name: string, code: string): BillingLocation {
    return {
      id, code, name, address: null, warehouseId: null, warehouseName: null, locationType: 'Standard',
      isHeadOffice: false, isActive: true, posMode: 'None',
    } as BillingLocation;
  }

  const templates = [
    template('inv-plain', 'Plain', { isDefault: true }),
    template('inv-ruled', 'Ruled', { layout: 'Traditional' }),
    template('inv-old', 'Old', { isActive: false }),
    template('quo-1', 'Quote', { documentType: 'Quotation', isDefault: true }),
    template('cn-1', 'Credit', { documentType: 'CreditNote', isDefault: true }),
  ];

  let fixture: ComponentFixture<PrintingTemplateListPage>;
  let sets: { locationId: string; templateId: string }[];
  let clears: { locationId: string; documentType: string }[];
  let refuseSet: boolean;

  async function render(locations: BillingLocation[], choices: PrintingTemplateLocationChoice[] = []): Promise<void> {
    sets = [];
    clears = [];
    refuseSet = false;
    const configuration: Partial<ConfigurationService> = {
      listPrintingTemplates: () => of(templates),
      listPrintElements: () => of([]),
      listCustomFieldDefinitions: () => of([]),
      listPrintingTemplateLocationChoices: () => of(choices),
      setPrintingTemplateLocationChoice: (_o: string, locationId: string, templateId: string): Observable<PrintingTemplateLocationChoice> => {
        if (refuseSet) {
          return throwError(() => ({ error: { title: "'Ruled' is inactive, so no location can print with it." } }));
        }

        sets.push({ locationId, templateId });
        return of({ locationId, documentType: 'Invoice', printingTemplateId: templateId });
      },
      clearPrintingTemplateLocationChoice: (_o: string, locationId: string, documentType: string): Observable<void> => {
        clears.push({ locationId, documentType });
        return of(undefined as void);
      },
    };
    const store: Partial<BillingLocationStore> = {
      locations: () => signal(locations).asReadonly(),
      // Sales-only scope: Quotation carries no location here.
      applies: (_o: string, type: string) => ['Invoice', 'SalesOrder', 'CreditNote'].includes(type),
    };

    await TestBed.configureTestingModule({
      imports: [PrintingTemplateListPage],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId }) } } },
        { provide: ConfigurationService, useValue: configuration },
        { provide: BillingLocationStore, useValue: store },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PrintingTemplateListPage);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function select(id: string): HTMLSelectElement {
    const found = el().querySelector<HTMLSelectElement>(`#${id}`);
    if (!found) {
      throw new Error(`no #${id}`);
    }

    return found;
  }

  function choose(id: string, value: string): void {
    const control = select(id);
    control.value = value;
    control.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('is not offered to an organization with one location', async () => {
    await render([location('ho', 'HeadOffice', 'HO')]);

    expect(el().textContent).not.toContain('By location');
    expect(el().querySelector('#printing-by-location-location')).toBeNull();
  });

  it('lists only the types that carry a location and have an active template, with only active templates', async () => {
    await render([location('ho', 'HeadOffice', 'HO'), location('br', 'Lakeside', '1002')]);

    expect(el().textContent).toContain('By location');
    expect(el().querySelector('#printing-by-location-Quotation')).toBeNull();
    expect(el().querySelector('#printing-by-location-SalesOrder')).toBeNull();

    const options = Array.from(select('printing-by-location-Invoice').options).map((o) => o.textContent?.trim());
    expect(options).toEqual(['Organization default (Plain)', 'Plain', 'Ruled']);
    expect(select('printing-by-location-CreditNote')).toBeTruthy();
  });

  it('shows the selected location\'s saved choice and switching location shows the other\'s', async () => {
    await render(
      [location('ho', 'HeadOffice', 'HO'), location('br', 'Lakeside', '1002')],
      [{ locationId: 'br', documentType: 'Invoice', printingTemplateId: 'inv-ruled' }],
    );

    expect(select('printing-by-location-Invoice').value).toBe('');

    choose('printing-by-location-location', 'br');
    expect(select('printing-by-location-Invoice').value).toBe('inv-ruled');
  });

  it('a template is a set and the default is a clear, each announced', async () => {
    await render([location('ho', 'HeadOffice', 'HO'), location('br', 'Lakeside', '1002')]);
    choose('printing-by-location-location', 'br');

    choose('printing-by-location-Invoice', 'inv-ruled');
    expect(sets).toEqual([{ locationId: 'br', templateId: 'inv-ruled' }]);
    expect(el().textContent).toContain('Lakeside now prints Invoice with Ruled.');

    choose('printing-by-location-Invoice', '');
    expect(clears).toEqual([{ locationId: 'br', documentType: 'Invoice' }]);
    expect(el().textContent).toContain("Lakeside now prints Invoice with the organization's default.");
  });

  it('a refused choice puts the select back and says why', async () => {
    await render([location('ho', 'HeadOffice', 'HO'), location('br', 'Lakeside', '1002')]);
    refuseSet = true;

    choose('printing-by-location-Invoice', 'inv-ruled');

    expect(select('printing-by-location-Invoice').value).toBe('');
    expect(sets).toEqual([]);
  });
});
