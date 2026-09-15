import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { ReportingTagOption } from '../../core/configuration/configuration.models';
import { ConfigurationService } from '../../core/configuration/configuration.service';
import { TransactionReportingTagDto } from '../../core/sales/sales.models';
import { ReportingTagsEditor } from './reporting-tags-editor';

/**
 * A reporting tag is <b>chosen, never typed</b>: the control is a multi-select over the
 * Category → Option pairs a tenant defines under Configurations → Reporting Tags.
 *
 * <p>On a tenant that has defined none, Add/Edit used to open a blank grey box beside a Save
 * button — nothing to pick, nothing to type, and no hint that the options live on another screen.
 * It read as a broken text field rather than an unconfigured picker. These assert the two states
 * are actually different, because the empty one is the one a new tenant meets first.</p>
 */
describe('ReportingTagsEditor', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const documentId = '22222222-2222-2222-2222-222222222222';

  let fixture: ComponentFixture<ReportingTagsEditor>;
  let saved: string[][];

  async function render(options: ReportingTagOption[], tags: TransactionReportingTagDto[] = []): Promise<void> {
    saved = [];

    const configuration: Partial<ConfigurationService> = {
      listReportingTagOptions: (): Observable<ReportingTagOption[]> => of(options),
      getTransactionReportingTags: (): Observable<TransactionReportingTagDto[]> => of(tags),
      setTransactionReportingTags: (_o: string, _t, _d: string, ids: string[]): Observable<void> => {
        saved.push(ids);
        return of(undefined as void);
      },
    };

    await TestBed.configureTestingModule({
      imports: [ReportingTagsEditor],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ConfigurationService, useValue: configuration },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReportingTagsEditor);
    fixture.componentRef.setInput('organizationId', organizationId);
    fixture.componentRef.setInput('documentType', 'SalesOrder');
    fixture.componentRef.setInput('documentId', documentId);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function openEditor(): void {
    Array.from(el().querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim().includes('Add/Edit'))!
      .click();
    fixture.detectChanges();
  }

  it('explains itself and points at the setup screen when no tags are defined', async () => {
    await render([]);
    openEditor();

    expect(el().textContent).toContain('no reporting tags yet');

    const link = el().querySelector<HTMLAnchorElement>('a[href$="/configuration/reporting-tags"]');
    expect(link).not.toBeNull();
    expect(link!.textContent).toContain('Set up Reporting Tags');
  });

  it('offers no Save when there is nothing that could be saved', async () => {
    await render([]);
    openEditor();

    const labels = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).map((b) =>
      b.textContent?.trim(),
    );

    // Save against an empty picker would write an empty set and look like it had done something.
    expect(labels).not.toContain('Save');
    expect(el().querySelector('select')).toBeNull();
  });

  it('renders the picker, with the multi-select hint, once options exist', async () => {
    await render([
      { id: 'o-1', organizationId, categoryId: 'c-1', name: 'Kathmandu', isActive: true, createdAt: '' },
      { id: 'o-2', organizationId, categoryId: 'c-1', name: 'Pokhara', isActive: true, createdAt: '' },
    ] as ReportingTagOption[]);
    openEditor();

    const select = el().querySelector<HTMLSelectElement>('select');
    expect(select).not.toBeNull();
    expect(select!.multiple).toBe(true);
    expect(Array.from(select!.options).map((o) => o.textContent?.trim())).toEqual([
      'Kathmandu',
      'Pokhara',
    ]);

    // The control is a multi-select; without this nobody discovers the second selection.
    expect(el().textContent).toContain('Hold Ctrl');
  });

  it('saves the chosen option ids', async () => {
    await render([
      { id: 'o-1', organizationId, categoryId: 'c-1', name: 'Kathmandu', isActive: true, createdAt: '' },
    ] as ReportingTagOption[]);
    openEditor();

    const select = el().querySelector<HTMLSelectElement>('select')!;
    select.options[0].selected = true;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    Array.from(el().querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => b.textContent?.trim() === 'Save')!
      .click();
    fixture.detectChanges();

    expect(saved).toEqual([['o-1']]);
  });
});
