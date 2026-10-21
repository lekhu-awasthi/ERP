import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of } from 'rxjs';

import {
  CustomFieldDefinition,
  PrintElementsForType,
  PrintLayoutOption,
  PrintingTemplate,
  PrintingTemplateLayoutRequest,
  PrintingTemplatePreviewRequest,
} from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PrintingTemplateLayoutEditor } from './printing-template-layout-editor';

/**
 * Phase 73 -- the layout editor (phase 79: its layouts, and what each leaves out, from the server's table too). What matters is what it sends: checked means printed, so an unticked box is a
 * hidden element; a locked element (a tax document's PAN) is drawn checked and disabled and never sent as hidden;
 * custom fields go in the definitions' order; and only the server's table decides which boxes exist.
 */
describe('PrintingTemplateLayoutEditor', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function template(over: Partial<PrintingTemplate> = {}): PrintingTemplate {
    return {
      id: 'tpl-1',
      organizationId,
      name: 'Standard',
      documentType: 'Invoice',
      isDefault: true,
      isActive: true,
      createdAt: '2026-10-09T00:00:00Z',
      layout: 'Standard',
      hiddenElements: [],
      printedCustomFieldIds: [],
      footerText: null,
      compactMargins: false,
      ...over,
    };
  }

  const a4 = [
    { layout: 'Standard', omits: [] },
    { layout: 'Traditional', omits: [] },
    { layout: 'Minimal', omits: ['OrganizationLogo'] },
    { layout: 'Modern', omits: [] },
    { layout: 'Classic', omits: [] },
  ] as const satisfies PrintLayoutOption[];

  const table: PrintElementsForType[] = [
    {
      documentType: 'Invoice',
      elements: ['OrganizationLogo', 'OrganizationAddress', 'OrganizationPan', 'PartyAddress', 'Unit', 'VatColumn', 'SignatureLine'],
      locked: ['OrganizationAddress', 'OrganizationPan', 'PartyAddress'],
      layouts: [...a4, { layout: 'Retail', omits: ['OrganizationLogo', 'SignatureLine'] }],
    },
    {
      documentType: 'JournalVoucher',
      elements: ['OrganizationLogo', 'OrganizationPan', 'SignatureLine'],
      locked: [],
      layouts: [...a4],
    },
  ];

  function field(id: string, name: string, over: Partial<CustomFieldDefinition> = {}): CustomFieldDefinition {
    return { id, name, type: 'Text', applicableDocumentTypes: ['Invoice'], choiceOptions: [], isActive: true, ...over };
  }

  let fixture: ComponentFixture<PrintingTemplateLayoutEditor>;
  let saved: { id: string; request: PrintingTemplateLayoutRequest }[];
  let previewed: PrintingTemplatePreviewRequest[];

  async function render(current: PrintingTemplate, fields: CustomFieldDefinition[] = []): Promise<void> {
    saved = [];
    previewed = [];
    const configuration: Partial<ConfigurationService> = {
      updatePrintingTemplateLayout: (_o: string, id: string, request: PrintingTemplateLayoutRequest): Observable<void> => {
        saved.push({ id, request });
        return of(undefined as void);
      },
      previewPrintingTemplate: (_o: string, request: PrintingTemplatePreviewRequest): Observable<Blob> => {
        previewed.push(request);
        return of(new Blob(['%PDF-1.7'], { type: 'application/pdf' }));
      },
    };

    await TestBed.configureTestingModule({
      imports: [PrintingTemplateLayoutEditor],
      providers: [{ provide: ConfigurationService, useValue: configuration }],
    }).compileComponents();

    fixture = TestBed.createComponent(PrintingTemplateLayoutEditor);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('template', current);
    fixture.componentRef.setInput('elementTable', table);
    fixture.componentRef.setInput('customFields', fields);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function box(id: string): HTMLInputElement {
    const input = el().querySelector<HTMLInputElement>(`#${id}`);
    if (!input) {
      throw new Error(`no #${id}`);
    }

    return input;
  }

  function click(id: string): void {
    box(id).click();
    fixture.detectChanges();
  }

  function button(text: string): HTMLButtonElement {
    const found = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.trim() === text);
    if (!found) {
      throw new Error(`no button ${text}`);
    }

    return found;
  }

  it('draws only the elements the server says the type prints', async () => {
    await render(template({ documentType: 'JournalVoucher' }));

    const ids = Array.from(el().querySelectorAll<HTMLInputElement>('input[id^="print-element-"]')).map((i) => i.id);

    expect(ids).toEqual(['print-element-OrganizationLogo', 'print-element-OrganizationPan', 'print-element-SignatureLine']);
  });

  it("locks a tax document's statutory elements checked, says why, and never sends them hidden", async () => {
    // A row hiding the PAN could only have been written by another door; the screen still cannot send it.
    await render(template({ hiddenElements: ['OrganizationPan'] }));

    const pan = box('print-element-OrganizationPan');
    expect(pan.checked).toBe(true);
    expect(pan.disabled).toBe(true);
    expect(pan.getAttribute('aria-describedby')).toBe('print-element-OrganizationPan-locked');
    expect(el().querySelector('#print-element-OrganizationPan-locked')?.textContent).toContain('required on a tax document');

    button('Save Layout').click();

    expect(saved[0].request.hiddenElements).toEqual([]);
  });

  it('sends an unticked box as hidden, with the layout, footer and margins chosen', async () => {
    await render(template());

    click('print-element-VatColumn');
    click('print-layout-Traditional');
    click('layout-editor-compact');
    const footer = el().querySelector<HTMLTextAreaElement>('#layout-editor-footer')!;
    footer.value = '  Bank: NIC Asia 0012345  ';
    footer.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    button('Save Layout').click();

    expect(saved).toEqual([{
      id: 'tpl-1',
      request: {
        layout: 'Traditional',
        hiddenElements: ['VatColumn'],
        printedCustomFieldIds: [],
        footerText: 'Bank: NIC Asia 0012345',
        compactMargins: true,
        printSerialNumbers: false,
      },
    }]);
  });

  it('offers a bill\'s template the serial numbers, off until ticked, and sends what was chosen (phase 95)', async () => {
    await render(template());

    const box = el().querySelector<HTMLInputElement>('#layout-editor-serials')!;
    expect(box.checked).toBe(false);
    expect(el().querySelector('label[for="layout-editor-serials"]')?.textContent).toContain('Print serial numbers');

    click('layout-editor-serials');
    button('Save Layout').click();

    expect(saved[0].request.printSerialNumbers).toBe(true);
  });

  it('offers no serial numbers to a type that sells no units, and leaves them alone on save (phase 95)', async () => {
    await render(template({ documentType: 'JournalVoucher' }));

    expect(el().querySelector('#layout-editor-serials')).toBeNull();
    button('Save Layout').click();

    expect(saved[0].request.printSerialNumbers).toBeNull();
  });

  it('starts from what is saved', async () => {
    await render(template({ layout: 'Minimal', hiddenElements: ['Unit'], footerText: 'Thanks', compactMargins: true }));

    expect(box('print-layout-Minimal').checked).toBe(true);
    expect(box('print-element-Unit').checked).toBe(false);
    expect(box('print-element-VatColumn').checked).toBe(true);
    expect(box('layout-editor-compact').checked).toBe(true);
    expect(el().querySelector<HTMLTextAreaElement>('#layout-editor-footer')!.value).toBe('Thanks');
  });

  it("offers only this type's active custom fields and sends them in the definitions' order", async () => {
    await render(template(), [
      field('cf-a', 'Site'),
      field('cf-b', 'Buyer PO'),
      field('cf-q', 'Quotation only', { applicableDocumentTypes: ['Quotation'] }),
      field('cf-x', 'Retired', { isActive: false }),
    ]);

    const offered = Array.from(el().querySelectorAll<HTMLInputElement>('input[id^="print-custom-field-"]')).map((i) => i.id);
    expect(offered).toEqual(['print-custom-field-cf-a', 'print-custom-field-cf-b']);

    click('print-custom-field-cf-b');
    click('print-custom-field-cf-a');
    button('Save Layout').click();

    expect(saved[0].request.printedCustomFieldIds).toEqual(['cf-a', 'cf-b']);
  });

  it('refuses a footer over 500 characters before sending anything', async () => {
    await render(template());

    const footer = el().querySelector<HTMLTextAreaElement>('#layout-editor-footer')!;
    footer.value = 'x'.repeat(501);
    footer.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(footer.getAttribute('aria-invalid')).toBe('true');
    expect(button('Save Layout').disabled).toBe(true);
    expect(button('Preview').disabled).toBe(true);
  });

  // ---- Phase 79: the layouts come from the server, and the roll leaves out what it has no room for ----

  it("offers the layouts the server lists for the type, the roll only where it is offered", async () => {
    await render(template());
    const invoice = Array.from(el().querySelectorAll<HTMLInputElement>('input[name="print-layout"]')).map((i) => i.id);
    expect(invoice).toEqual([
      'print-layout-Standard', 'print-layout-Traditional', 'print-layout-Minimal', 'print-layout-Modern',
      'print-layout-Classic', 'print-layout-Retail',
    ]);

    TestBed.resetTestingModule();
    await render(template({ documentType: 'JournalVoucher' }));
    expect(el().querySelector('#print-layout-Retail')).toBeNull();
    expect(el().querySelector('#print-layout-Classic')).not.toBeNull();
  });

  it('greys out what the roll never prints, says why, and keeps the saved choice for when the layout changes back', async () => {
    // The signature line was hidden on purpose; the logo was not.
    await render(template({ hiddenElements: ['SignatureLine'] }));

    click('print-layout-Retail');

    const logo = box('print-element-OrganizationLogo');
    expect(logo.checked).toBe(false);
    expect(logo.disabled).toBe(true);
    expect(logo.getAttribute('aria-describedby')).toBe('print-element-OrganizationLogo-omitted');
    expect(el().querySelector('#print-element-OrganizationLogo-omitted')?.textContent).toContain('not on this layout');
    expect(box('print-element-Unit').disabled).toBe(false);

    button('Save Layout').click();
    expect(saved[0].request.layout).toBe('Retail');
    expect(saved[0].request.hiddenElements).toEqual(['SignatureLine']);

    click('print-layout-Classic');
    expect(box('print-element-OrganizationLogo').checked).toBe(true);
    expect(box('print-element-OrganizationLogo').disabled).toBe(false);
    expect(box('print-element-SignatureLine').checked).toBe(false);
  });

  it('previews the unsaved settings for its own type and shows the PDF in a titled frame', async () => {
    await render(template());

    click('print-element-Unit');
    button('Preview').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(previewed[0].documentType).toBe('Invoice');
    expect(previewed[0].hiddenElements).toEqual(['Unit']);
    expect(saved).toEqual([]);
    const frame = el().querySelector('iframe');
    expect(frame?.getAttribute('title')).toBe('Preview of the Standard layout with sample data');
  });
});
