import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';

import { InboxDocument, InboxTargetType } from '../../core/workflow/inbox.models';
import { InboxService } from '../../core/workflow/inbox.service';
import { triggerBlobDownload } from '../download-file';

/**
 * Phase 22 (FR-10.3), <b>exit criterion #2</b>: "the source image stays linked and viewable from the
 * resulting document" is a requirement on the <i>transaction's</i> detail page, not only on the
 * inbox. This is that surface -- drop it onto any transaction detail page and it shows the scan the
 * document was typed from, if there is one.
 *
 * <p>Renders nothing at all when no inbox document points at the transaction, which is the common
 * case for a document somebody typed from scratch. A user who cannot read the inbox
 * (<code>Workflow.InboxDocument.View</code>) gets the same empty render rather than an error --
 * this is a supplementary panel, and failing it must not break the page it sits on.</p>
 *
 * <p>The preview is an authenticated API route, not a public URL: <code>IFileStorage</code>
 * deliberately exposes none, so the browser's own cookie-bearing request to
 * <code>.../content</code> is what makes the image render, and it is permission-checked like every
 * other read.</p>
 */
@Component({
  selector: 'app-source-document-panel',
  imports: [RouterLink, DatePipe],
  templateUrl: './source-document-panel.html',
})
export class SourceDocumentPanel {
  private readonly inboxService = inject(InboxService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly organizationId = input.required<string>();
  readonly transactionType = input.required<InboxTargetType>();
  /** Empty on a `new` route, where there is no transaction yet -- the panel simply renders nothing
   * until the document has been saved and the id exists. */
  readonly transactionId = input<string | null>(null);

  protected readonly documents = signal<InboxDocument[]>([]);

  constructor() {
    effect(() => {
      const transactionId = this.transactionId();
      if (!transactionId) {
        this.documents.set([]);
        return;
      }

      this.inboxService
        .listDocuments(this.organizationId(), {
          linkedTransactionType: this.transactionType(),
          linkedTransactionId: transactionId,
          pageSize: 10,
        })
        .subscribe({
          next: (result) => this.documents.set(result.items),
          error: () => this.documents.set([]),
        });
    });
  }

  protected previewUrl(document: InboxDocument): string {
    return this.inboxService.contentUrl(this.organizationId(), document.id);
  }

  protected previewResourceUrl(document: InboxDocument): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(this.previewUrl(document));
  }

  protected isImage(document: InboxDocument): boolean {
    return /\.(png|jpe?g|gif|webp)$/i.test(document.fileName);
  }

  protected isPdf(document: InboxDocument): boolean {
    return /\.pdf$/i.test(document.fileName);
  }

  protected inboxRoute(): string[] {
    return ['/organizations', this.organizationId(), 'workflow', 'document-inbox'];
  }

  protected download(document: InboxDocument): void {
    this.inboxService
      .downloadDocument(this.organizationId(), document.id)
      .subscribe({ next: (blob) => triggerBlobDownload(blob, document.fileName) });
  }
}
