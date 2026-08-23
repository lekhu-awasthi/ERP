import { Component, effect, inject, input, signal } from '@angular/core';

import { ConfigurationService } from '../../core/configuration/configuration.service';
import { ReportingTagOption } from '../../core/configuration/configuration.models';
import { TransactionReportingTagDto, DocumentType } from '../../core/sales/sales.models';
import { extractErrorMessage } from '../../core/auth/api-error';

/**
 * Phase 19 decision #1 -- document-level Reporting Tags editor, live-confirmed against the real
 * Tigg detail page ("REPORTING TAGS ... Add/Edit ... No reporting tags"). Reused across every
 * document type Reporting Tags are confirmed to carry (Quotation, Invoice). Only shown for a
 * saved document (documentId set) -- tags are a post-creation edit action, not a create-time field
 * (see SetTransactionReportingTagsCommand's doc comment).
 */
@Component({
  selector: 'app-reporting-tags-editor',
  imports: [],
  templateUrl: './reporting-tags-editor.html',
})
export class ReportingTagsEditor {
  private readonly configurationService = inject(ConfigurationService);

  readonly organizationId = input.required<string>();
  readonly documentType = input.required<DocumentType>();
  readonly documentId = input.required<string>();
  readonly editable = input<boolean>(true);

  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly tags = signal<TransactionReportingTagDto[]>([]);
  protected readonly allOptions = signal<ReportingTagOption[]>([]);
  protected readonly selectedIds = signal<string[]>([]);

  constructor() {
    effect(() => {
      const organizationId = this.organizationId();
      const documentId = this.documentId();
      if (!documentId) {
        return;
      }
      this.load(organizationId, documentId);
    });
  }

  protected startEditing(): void {
    this.configurationService.listReportingTagOptions(this.organizationId()).subscribe({
      next: (options) => {
        this.allOptions.set(options);
        this.selectedIds.set(this.tags().map((t) => t.tagOptionId));
        this.editing.set(true);
      },
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load reporting tag options.'),
    });
  }

  protected cancelEditing(): void {
    this.editing.set(false);
  }

  protected onSelectionChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.selectedIds.set(Array.from(select.selectedOptions).map((o) => o.value));
  }

  protected save(): void {
    this.saving.set(true);
    this.errorMessage.set(null);

    this.configurationService
      .setTransactionReportingTags(this.organizationId(), this.documentType(), this.documentId(), this.selectedIds())
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editing.set(false);
          this.load(this.organizationId(), this.documentId());
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save reporting tags.');
        },
      });
  }

  private load(organizationId: string, documentId: string): void {
    this.configurationService.getTransactionReportingTags(organizationId, this.documentType(), documentId).subscribe({
      next: (tags) => this.tags.set(tags),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load reporting tags.'),
    });
  }
}
