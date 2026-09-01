import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  AiDocumentExtractionSetting,
  INBOX_TARGET_LABELS,
  INBOX_TARGET_TYPES,
  InboxDocument,
  InboxTargetType,
  UploadedDocumentStatus,
} from '../../../core/workflow/inbox.models';
import { InboxService } from '../../../core/workflow/inbox.service';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Phase 22 (FR-10.3) -- Workflow > Document. Lives beside the Transaction Approval queue, matching
 * the reference product's own Workflow module (erp-module-scan.md's second sub-module, between
 * Tasks and Transaction Approval).
 *
 * <p>Pending/Done are <b>tabs</b>, as in the reference product, implemented as a server-side status
 * filter -- a client-side split would show a page's worth of rows rather than the tab's worth
 * (phase-16c's bug #1, the same class of mistake).</p>
 *
 * <p>"+ Add as" navigates to the target's own <code>new</code> route carrying
 * <code>?inboxDocumentId=</code>. It creates nothing: the user reviews a pre-filled ordinary form
 * and presses Save, and that form's own Create command runs through the whole pipeline as usual
 * (docs/phase-22-status.md, Decision B). Carrying the id in the <b>URL</b> rather than in an
 * in-memory store is deliberate -- PendingTemplateStore is read-once and does not survive a reload,
 * which for a conversion the user may sit on for several minutes is a real loss.</p>
 */
@Component({
  selector: 'app-document-inbox-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './document-inbox-page.html',
})
export class DocumentInboxPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly inboxService = inject(InboxService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly targetTypes = INBOX_TARGET_TYPES;
  protected readonly targetLabels = INBOX_TARGET_LABELS;

  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly busyDocumentId = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly documents = signal<InboxDocument[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly status = signal<UploadedDocumentStatus>('Pending');
  protected readonly search = signal('');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly extractionSetting = signal<AiDocumentExtractionSetting | null>(null);
  /** Which row's "+ Add as" menu is open. Bootstrap's dropdown JS is not loaded in this app (only
   * its stylesheet -- see angular.json), so the menu is driven from here. */
  protected readonly openMenuId = signal<string | null>(null);

  /**
   * Viewport coordinates for the open menu, so it can be rendered `position: fixed`.
   *
   * <p>Absolute positioning does not work here: the grid sits in a `.table-responsive`, whose
   * `overflow-x: auto` makes the browser compute `overflow-y: auto` too, and the menu is then
   * clipped at the wrapper's bottom edge. Caught in Phase 22's browser pass with three of the four
   * conversion targets in the DOM but unreachable. A fixed-position element escapes ancestor
   * overflow entirely.</p>
   */
  protected readonly menuPosition = signal<{ top: number; left: number } | null>(null);
  protected readonly savingSetting = signal(false);

  protected readonly selected = computed(() => this.documents().find((d) => d.id === this.selectedId()) ?? null);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));

  /** Both gates must be open for the Extract button to do anything: the deployment must have a
   * credential, and this tenant must have opted in. The template names whichever is missing. */
  protected readonly extractionAvailable = computed(() => {
    const setting = this.extractionSetting();
    return !!setting && setting.enabled && setting.extractorConfigured;
  });

  constructor() {
    this.load();
    this.inboxService.getExtractionSetting(this.organizationId).subscribe({
      next: (setting) => this.extractionSetting.set(setting),
      // A user without permission to read the setting still gets a working inbox -- extraction is
      // the optional half, so this failure is silent by design.
      error: () => this.extractionSetting.set(null),
    });
  }

  protected load(): void {
    this.closeAddAsMenu();
    this.loading.set(true);
    this.errorMessage.set(null);

    this.inboxService
      .listDocuments(this.organizationId, {
        status: this.status(),
        search: this.search() || null,
        page: this.page(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (result) => {
          this.loading.set(false);
          this.documents.set(result.items);
          this.totalCount.set(result.totalCount);
          if (!result.items.some((d) => d.id === this.selectedId())) {
            this.selectedId.set(result.items[0]?.id ?? null);
          }
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the document inbox.');
        },
      });
  }

  protected showTab(status: UploadedDocumentStatus): void {
    this.status.set(status);
    this.page.set(1);
    this.load();
  }

  protected onSearchChange(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.page.set(page);
    this.load();
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.errorMessage.set(null);

    this.inboxService.uploadDocument(this.organizationId, file).subscribe({
      next: (document) => {
        this.uploading.set(false);
        input.value = '';
        // A fresh upload is always Pending, so show that tab rather than silently uploading into a
        // tab the user is not looking at.
        this.status.set('Pending');
        this.page.set(1);
        this.selectedId.set(document.id);
        this.load();
      },
      error: (err: unknown) => {
        this.uploading.set(false);
        input.value = '';
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not upload the document.');
      },
    });
  }

  protected previewUrl(document: InboxDocument): string {
    return this.inboxService.contentUrl(this.organizationId, document.id);
  }

  /** Angular sanitizes an iframe's src as a resource URL and blocks an interpolated string
   * outright. The URL is built entirely from our own API base plus a route-parameter GUID -- no
   * user-supplied text reaches it -- so bypassing is safe here and is the only way to render a PDF
   * inline. An <img> src needs no such treatment. */
  protected previewResourceUrl(document: InboxDocument): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(this.previewUrl(document));
  }

  protected toggleAddAsMenu(id: string, event: MouseEvent): void {
    if (this.openMenuId() === id) {
      this.closeAddAsMenu();
      return;
    }

    const button = event.currentTarget as HTMLElement;
    const rect = button.getBoundingClientRect();
    // Right-aligned under the button, matching dropdown-menu-end's intent.
    this.menuPosition.set({ top: Math.round(rect.bottom + 2), left: Math.round(rect.right) });
    this.openMenuId.set(id);
  }

  protected closeAddAsMenu(): void {
    this.openMenuId.set(null);
    this.menuPosition.set(null);
  }

  protected isImage(document: InboxDocument): boolean {
    return /\.(png|jpe?g|gif|webp)$/i.test(document.fileName);
  }

  protected isPdf(document: InboxDocument): boolean {
    return /\.pdf$/i.test(document.fileName);
  }

  protected download(document: InboxDocument): void {
    this.inboxService.downloadDocument(this.organizationId, document.id).subscribe({
      next: (blob) => triggerBlobDownload(blob, document.fileName),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not download the document.'),
    });
  }

  protected extract(document: InboxDocument): void {
    this.runOnDocument(document.id, this.inboxService.extract(this.organizationId, document.id));
  }

  protected clearExtraction(document: InboxDocument): void {
    this.runOnDocument(document.id, this.inboxService.clearExtraction(this.organizationId, document.id));
  }

  protected setStatus(document: InboxDocument, status: UploadedDocumentStatus): void {
    this.runOnDocument(
      document.id,
      this.inboxService.updateDocument(this.organizationId, document.id, {
        description: document.description,
        label: document.label,
        status,
      }),
    );
  }

  protected remove(document: InboxDocument): void {
    if (!window.confirm(`Delete "${document.fileName}"? The file is removed permanently.`)) {
      return;
    }

    this.busyDocumentId.set(document.id);
    this.errorMessage.set(null);

    this.inboxService.deleteDocument(this.organizationId, document.id).subscribe({
      next: () => {
        this.busyDocumentId.set(null);
        this.selectedId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.busyDocumentId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete the document.');
      },
    });
  }

  /**
   * Navigates to the target's own `new` route with `?inboxDocumentId=`. Nothing is created here --
   * the target page fetches the prefill, the user reviews it, and their Save runs the ordinary
   * Create command.
   */
  protected addAs(document: InboxDocument, targetType: InboxTargetType): void {
    this.closeAddAsMenu();
    this.router.navigate(this.newRouteFor(targetType), { queryParams: { inboxDocumentId: document.id } });
  }

  protected linkedRoute(document: InboxDocument): string[] | null {
    if (!document.isLinked || !document.linkedTransactionType || !document.linkedTransactionId) {
      return null;
    }

    const org = this.organizationId;
    switch (document.linkedTransactionType) {
      case 'Invoice':
        return ['/organizations', org, 'sales', 'invoices', document.linkedTransactionId];
      case 'PurchaseBill':
        return ['/organizations', org, 'purchasing', 'purchase-bills', document.linkedTransactionId];
      case 'Expense':
        return ['/organizations', org, 'purchasing', 'expenses', document.linkedTransactionId];
      // Quick Payment approves on save (Phase 17's screen), so the resulting row is a Payment. A
      // supplier payment lives on its own route; the receipt side on the customer-payment one. The
      // inbox does not know the direction, so it links to the customer-payment route, which is the
      // one Quick Receipt produces -- the Payments list reaches the other.
      case 'Payment':
        return ['/organizations', org, 'payments', document.linkedTransactionId];
    }
  }

  protected toggleExtractionSetting(event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    this.savingSetting.set(true);
    this.errorMessage.set(null);

    this.inboxService.updateExtractionSetting(this.organizationId, enabled).subscribe({
      next: (setting) => {
        this.savingSetting.set(false);
        this.extractionSetting.set(setting);
      },
      error: (err: unknown) => {
        this.savingSetting.set(false);
        this.errorMessage.set(
          extractErrorMessage(err) ?? 'Could not change the AI extraction setting. An Admin can change it.',
        );
      },
    });
  }

  private newRouteFor(targetType: InboxTargetType): string[] {
    const org = this.organizationId;
    switch (targetType) {
      case 'Invoice':
        return ['/organizations', org, 'sales', 'invoices', 'new'];
      case 'PurchaseBill':
        return ['/organizations', org, 'purchasing', 'purchase-bills', 'new'];
      case 'Expense':
        return ['/organizations', org, 'purchasing', 'expenses', 'new'];
      case 'Payment':
        return ['/organizations', org, 'quick-payment'];
    }
  }

  private runOnDocument(id: string, request: ReturnType<InboxService['extract']>): void {
    this.busyDocumentId.set(id);
    this.errorMessage.set(null);

    request.subscribe({
      next: (updated) => {
        this.busyDocumentId.set(null);
        this.documents.update((docs) => docs.map((d) => (d.id === updated.id ? updated : d)));
        // A hand-filed or reopened document may have moved off the tab being viewed.
        if (updated.status !== this.status()) {
          this.load();
        }
      },
      error: (err: unknown) => {
        this.busyDocumentId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'The action could not be completed.');
      },
    });
  }
}
