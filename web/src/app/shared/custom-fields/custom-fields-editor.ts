import { Component, effect, inject, input, signal } from '@angular/core';
import { Observable, of } from 'rxjs';

import { ConfigurationService } from '../../core/configuration/configuration.service';
import { CustomFieldDefinition } from '../../core/configuration/configuration.models';
import { CustomFieldValueInput, DocumentType } from '../../core/sales/sales.models';
import { extractErrorMessage } from '../../core/auth/api-error';

/**
 * Phase 20a -- the deferred write-side half of Phase 2's EAV Custom Fields (docs/phase-20a-status.md).
 * Unlike ReportingTagsEditor, this renders inline in the document's own create/edit form and is
 * always editable (no "Add/Edit" gate, no Draft-vs-Approved lock) -- both live-confirmed against the
 * real Tigg "Add New Invoice" form and its Edit-on-an-Approved-Invoice form. DocumentId doesn't exist
 * yet on a create form, so this component can't save itself the way ReportingTagsEditor does; the
 * parent page calls commitTo(documentId) right after its own Create/Update succeeds, under the same
 * single "Save" click from the user's perspective.
 */
@Component({
  selector: 'app-custom-fields-editor',
  imports: [],
  templateUrl: './custom-fields-editor.html',
})
export class CustomFieldsEditor {
  private readonly configurationService = inject(ConfigurationService);

  readonly organizationId = input.required<string>();
  readonly documentType = input.required<DocumentType>();
  readonly documentId = input<string | null>(null);

  protected readonly errorMessage = signal<string | null>(null);
  protected readonly definitions = signal<CustomFieldDefinition[]>([]);
  private readonly values = signal<Record<string, string>>({});

  constructor() {
    effect(() => {
      const organizationId = this.organizationId();
      const documentType = this.documentType();
      this.configurationService.listCustomFieldDefinitions(organizationId).subscribe({
        next: (all) =>
          this.definitions.set(all.filter((d) => d.isActive && d.applicableDocumentTypes.includes(documentType))),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load custom fields.'),
      });
    });

    effect(() => {
      const organizationId = this.organizationId();
      const documentType = this.documentType();
      const documentId = this.documentId();
      if (!documentId) {
        this.values.set({});
        return;
      }
      this.configurationService.getCustomFieldValues(organizationId, documentType, documentId).subscribe({
        next: (rows) => this.values.set(Object.fromEntries(rows.map((r) => [r.fieldDefinitionId, r.value]))),
        error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load custom field values.'),
      });
    });
  }

  protected valueFor(fieldId: string): string {
    return this.values()[fieldId] ?? '';
  }

  protected onValueChange(fieldId: string, event: Event): void {
    const value = (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
    this.values.update((v) => ({ ...v, [fieldId]: value }));
  }

  /** Replaces the whole value set for `documentId` with whatever is currently entered. A no-op
   * (no HTTP call) when this document type has no applicable custom fields defined. */
  commitTo(documentId: string): Observable<void> {
    const definitions = this.definitions();
    if (definitions.length === 0) {
      return of(undefined);
    }

    const values: CustomFieldValueInput[] = definitions.map((d) => ({
      fieldDefinitionId: d.id,
      value: this.valueFor(d.id),
    }));
    return this.configurationService.setCustomFieldValues(this.organizationId(), this.documentType(), documentId, values);
  }
}
