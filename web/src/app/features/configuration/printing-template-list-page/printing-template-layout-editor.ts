import {
  AfterViewInit, Component, ElementRef, OnDestroy, OnInit, computed, inject, input, output, signal, viewChild,
} from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  CustomFieldDefinition,
  PrintElement,
  PrintElementsForType,
  PrintLayout,
  PrintingTemplate,
  PrintingTemplateLayoutRequest,
} from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/** The three fixed layouts (phase 73 Decision A), each with the one line that tells them apart. */
export const PRINT_LAYOUTS: readonly { value: PrintLayout; label: string; description: string }[] = [
  { value: 'Standard', label: 'Standard', description: 'Centred header with the logo beside it, plain tables. What every document printed before.' },
  { value: 'Traditional', label: 'Traditional', description: 'Every block ruled: a boxed header, a gridded table, totals in a ruled column.' },
  { value: 'Minimal', label: 'Minimal', description: 'Flush-left header, no logo, no rules but the table header, smaller type.' },
];

/** How each element reads on the screen, and the group it sits in. Which elements a type offers, and which it may
 * not hide, come from the server (`listPrintElements`), never from this table. */
const ELEMENT_LABELS: Record<PrintElement, string> = {
  OrganizationLogo: 'Logo',
  OrganizationAddress: 'Address',
  OrganizationPhone: 'Phone',
  OrganizationEmail: 'Email',
  OrganizationPan: 'PAN',
  OrganizationWebsite: 'Website',
  PartyAddress: 'Customer or supplier address',
  Reference: 'Reference',
  ItemCode: 'Item code before the product name',
  HsCode: 'HS code (when a line has one)',
  Unit: 'Unit',
  DiscountColumn: 'Discount column',
  VatColumn: 'VAT column',
  AmountInWords: 'Amount in words',
  Notes: 'Notes',
  Terms: 'Terms and conditions',
  SignatureLine: 'Prepared By / Approved By',
};

const ELEMENT_GROUPS: readonly { legend: string; elements: readonly PrintElement[] }[] = [
  {
    legend: 'Organization',
    elements: ['OrganizationLogo', 'OrganizationAddress', 'OrganizationPhone', 'OrganizationEmail', 'OrganizationPan', 'OrganizationWebsite'],
  },
  { legend: 'Document', elements: ['PartyAddress', 'Reference'] },
  { legend: 'Lines', elements: ['ItemCode', 'HsCode', 'Unit', 'DiscountColumn', 'VatColumn'] },
  { legend: 'Totals and footer', elements: ['AmountInWords', 'Notes', 'Terms', 'SignatureLine'] },
];

export const FOOTER_TEXT_MAX_LENGTH = 500;

/**
 * Phase 73 -- one template's layout: which fixed layout, what it shows, which custom fields it prints, a footer line
 * and compact margins, with a preview of the unsaved settings over a sample document. Checked means printed; an
 * element a tax document must print (VAT Rules Schedule 5) is checked and disabled, with the reason beside it.
 *
 * State is in signals written by each control's own handler (the app is zoneless: a computed over a FormControl's
 * value would never update, phase 17).
 */
@Component({
  selector: 'app-printing-template-layout-editor',
  imports: [StatusBanner],
  templateUrl: './printing-template-layout-editor.html',
})
export class PrintingTemplateLayoutEditor implements OnInit, AfterViewInit, OnDestroy {
  private readonly configurationService = inject(ConfigurationService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly organizationId = input.required<string>();
  readonly template = input.required<PrintingTemplate>();
  readonly elementTable = input.required<PrintElementsForType[]>();
  readonly customFields = input.required<CustomFieldDefinition[]>();
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly layouts = PRINT_LAYOUTS;
  protected readonly footerMaxLength = FOOTER_TEXT_MAX_LENGTH;

  protected readonly layout = signal<PrintLayout>('Standard');
  protected readonly hidden = signal<ReadonlySet<PrintElement>>(new Set());
  protected readonly printedCustomFieldIds = signal<readonly string[]>([]);
  protected readonly footerText = signal('');
  protected readonly compactMargins = signal(false);

  protected readonly saving = signal(false);
  protected readonly previewing = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly previewUrl = signal<SafeResourceUrl | null>(null);
  private previewObjectUrl: string | null = null;
  private readonly heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  /** This template's type's row of the server's element table. */
  private readonly forType = computed(() =>
    this.elementTable().find((row) => row.documentType === this.template().documentType) ?? null);

  /** The groups, narrowed to what this type prints; an empty group is not drawn. */
  protected readonly groups = computed(() => {
    const row = this.forType();
    const offered = new Set(row?.elements ?? []);
    const locked = new Set(row?.locked ?? []);
    return ELEMENT_GROUPS
      .map((group) => ({
        legend: group.legend,
        elements: group.elements
          .filter((element) => offered.has(element))
          .map((element) => ({ element, label: ELEMENT_LABELS[element], locked: locked.has(element) })),
      }))
      .filter((group) => group.elements.length > 0);
  });

  /** The custom fields that apply to this type, active ones only, in the definitions' own order. */
  protected readonly applicableCustomFields = computed(() =>
    this.customFields().filter((field) => field.isActive && field.applicableDocumentTypes.includes(this.template().documentType)));

  protected readonly footerTooLong = computed(() => this.footerText().length > FOOTER_TEXT_MAX_LENGTH);

  /** The controls start from what is saved. The page creates one editor per template it opens. */
  ngOnInit(): void {
    const template = this.template();
    this.layout.set(template.layout ?? 'Standard');
    this.hidden.set(new Set(template.hiddenElements ?? []));
    this.printedCustomFieldIds.set(template.printedCustomFieldIds ?? []);
    this.footerText.set(template.footerText ?? '');
    this.compactMargins.set(template.compactMargins ?? false);
  }

  /** The editor opens above the list, away from the row's Layout button that opened it, so focus moves to its
   * heading; a keyboard user would otherwise be left below it with no sign it appeared (the phase's keyboard pass). */
  ngAfterViewInit(): void {
    this.heading().nativeElement.focus();
  }

  protected shows(element: PrintElement): boolean {
    return !this.hidden().has(element);
  }

  protected toggleElement(element: PrintElement, event: Event): void {
    const shown = (event.target as HTMLInputElement).checked;
    const next = new Set(this.hidden());
    if (shown) {
      next.delete(element);
    } else {
      next.add(element);
    }

    this.hidden.set(next);
  }

  protected prints(fieldId: string): boolean {
    return this.printedCustomFieldIds().includes(fieldId);
  }

  /** Ticked fields print in the definitions' order, whatever order they were ticked in. */
  protected toggleCustomField(fieldId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const chosen = new Set(this.printedCustomFieldIds());
    if (checked) {
      chosen.add(fieldId);
    } else {
      chosen.delete(fieldId);
    }

    this.printedCustomFieldIds.set(this.applicableCustomFields().map((field) => field.id).filter((id) => chosen.has(id)));
  }

  protected onLayout(layout: PrintLayout): void {
    this.layout.set(layout);
  }

  protected onFooter(event: Event): void {
    this.footerText.set((event.target as HTMLTextAreaElement).value);
  }

  protected onCompact(event: Event): void {
    this.compactMargins.set((event.target as HTMLInputElement).checked);
  }

  protected save(): void {
    if (this.footerTooLong()) {
      return;
    }

    this.saving.set(true);
    this.message.set(null);
    this.errorMessage.set(null);
    this.configurationService
      .updatePrintingTemplateLayout(this.organizationId(), this.template().id, this.request())
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set(`Layout saved for ${this.template().name}.`);
          this.saved.emit();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the layout. Please try again.');
        },
      });
  }

  protected preview(): void {
    if (this.footerTooLong()) {
      return;
    }

    this.previewing.set(true);
    this.errorMessage.set(null);
    this.configurationService
      .previewPrintingTemplate(this.organizationId(), { documentType: this.template().documentType, ...this.request() })
      .subscribe({
        next: (blob) => {
          this.previewing.set(false);
          this.clearPreview();
          this.previewObjectUrl = URL.createObjectURL(blob);
          // A blob: URL this page just minted from the server's own PDF, never anything a user typed.
          this.previewUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.previewObjectUrl));
          this.message.set('Preview updated with sample data.');
        },
        error: (err: unknown) => {
          this.previewing.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not draw the preview. Please try again.');
        },
      });
  }

  protected close(): void {
    this.clearPreview();
    this.closed.emit();
  }

  ngOnDestroy(): void {
    this.clearPreview();
  }

  private request(): PrintingTemplateLayoutRequest {
    const footer = this.footerText().trim();
    return {
      layout: this.layout(),
      hiddenElements: [...this.hidden()].filter((element) =>
        this.forType()?.elements.includes(element) && !this.forType()?.locked.includes(element)),
      printedCustomFieldIds: [...this.printedCustomFieldIds()],
      footerText: footer.length > 0 ? footer : null,
      compactMargins: this.compactMargins(),
    };
  }

  private clearPreview(): void {
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = null;
    }

    this.previewUrl.set(null);
  }
}
