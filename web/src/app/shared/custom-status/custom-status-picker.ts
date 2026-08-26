import { Component, inject, input, output, signal } from '@angular/core';

import { ConfigurationService } from '../../core/configuration/configuration.service';
import { CustomStatus } from '../../core/configuration/configuration.models';
import { DocumentType } from '../../core/sales/sales.models';

/**
 * Phase 20b -- per-row Custom Status picker, live-confirmed against the real Tigg tenant to be a
 * THIRD shape distinct from both app-custom-fields-editor (inline in the document's own form) and
 * app-reporting-tags-editor (a sidebar "Add/Edit" action on the detail page): this control lives
 * only in the Quotation/Purchase Order LIST grid (a "Stage" column per row) and has no presence on
 * the detail page at all. Applies instantly on selection -- no separate Save action.
 *
 * `options` is passed in by the parent (loaded once per page, not once per row) rather than
 * self-loaded, since a self-loading effect here would fire one HTTP call per rendered row.
 */
@Component({
  selector: 'app-custom-status-picker',
  imports: [],
  templateUrl: './custom-status-picker.html',
})
export class CustomStatusPicker {
  private readonly configurationService = inject(ConfigurationService);

  readonly organizationId = input.required<string>();
  readonly documentType = input.required<DocumentType>();
  readonly documentId = input.required<string>();
  readonly customStatusId = input.required<string | null>();
  readonly options = input.required<CustomStatus[]>();

  readonly statusChange = output<string | null>();

  protected readonly saving = signal(false);

  protected onChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const value = select.value.length > 0 ? select.value : null;

    this.saving.set(true);
    this.configurationService
      .setCustomStatus(this.organizationId(), this.documentType(), this.documentId(), value)
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.statusChange.emit(value);
        },
        error: () => this.saving.set(false),
      });
  }
}
