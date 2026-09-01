import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PagedResult } from '../../core/common/paged-result';
import { InboxDocument } from '../../core/workflow/inbox.models';
import { InboxService } from '../../core/workflow/inbox.service';
import { SourceDocumentPanel } from './source-document-panel';

/**
 * Phase 22, <b>exit criterion #2</b>: "the source image stays linked and viewable from the resulting
 * document." That is a requirement on the transaction's detail page, so this asserts the panel that
 * carries it -- including the two ways it must stay out of the way: no linked document renders
 * nothing, and a failed lookup renders nothing rather than breaking the page it sits on.
 */
describe('SourceDocumentPanel', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const purchaseBillId = '22222222-2222-2222-2222-222222222222';

  @Component({
    imports: [SourceDocumentPanel],
    template: `<app-source-document-panel
      [organizationId]="organizationId"
      transactionType="PurchaseBill"
      [transactionId]="transactionId"
    />`,
  })
  class Host {
    organizationId = organizationId;
    transactionId: string | null = purchaseBillId;
  }

  function document(overrides: Partial<InboxDocument> = {}): InboxDocument {
    return {
      id: 'doc-1',
      fileName: 'supplier-bill.jpg',
      sizeBytes: 20480,
      contentType: 'image/jpeg',
      description: null,
      label: null,
      status: 'Done',
      uploadedByUserId: 'user-1',
      uploadedByName: 'Ram Bahadur',
      uploadedAt: '2026-09-01T07:39:46Z',
      isLinked: true,
      linkedTransactionType: 'PurchaseBill',
      linkedTransactionId: purchaseBillId,
      linkedAt: '2026-09-01T08:00:00Z',
      extractionStatus: 'NotAttempted',
      extractionModelId: null,
      extractionFailureReason: null,
      extractionAttemptedAt: null,
      isExtractable: true,
      extractedData: null,
      ...overrides,
    };
  }

  function host(stub: InboxServiceStub, transactionId: string | null = purchaseBillId) {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InboxService, useValue: stub },
      ],
    });

    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.transactionId = transactionId;
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    return { fixture, element, text: () => element().textContent ?? '' };
  }

  it('shows the linked scan, its uploader, and an inline preview', () => {
    const stub = new InboxServiceStub([document()]);
    const { text, element } = host(stub);

    expect(text()).toContain('Source document');
    expect(text()).toContain('supplier-bill.jpg');
    expect(text()).toContain('Ram Bahadur');
    expect(element().querySelector('img')).not.toBeNull();

    // The preview points at the authenticated API route, never a public storage URL.
    expect(element().querySelector('img')?.getAttribute('src')).toContain('/inbox-documents/doc-1/content');
  });

  it('marks a scan whose values were read by AI', () => {
    const stub = new InboxServiceStub([
      document({ extractionStatus: 'Succeeded', extractionModelId: 'claude-opus-5' }),
    ]);
    expect(host(stub).text()).toContain('AI-assisted');
  });

  it('renders nothing when no inbox document points at the transaction', () => {
    const { text } = host(new InboxServiceStub([]));
    expect(text().trim()).toBe('');
  });

  it('renders nothing when the transaction has not been saved yet', () => {
    const stub = new InboxServiceStub([document()]);
    const { text } = host(stub, null);

    expect(text().trim()).toBe('');
    expect(stub.callCount).toBe(0);
  });

  /** This is a supplementary panel on somebody else's page. A user without inbox permission, or any
   * other failure, must leave that page working. */
  it('renders nothing rather than breaking the page when the lookup fails', () => {
    const { text } = host(new InboxServiceStub([], true));
    expect(text().trim()).toBe('');
  });

  it('asks for exactly the documents linked to this transaction', () => {
    const stub = new InboxServiceStub([document()]);
    host(stub);

    expect(stub.lastOptions?.linkedTransactionType).toBe('PurchaseBill');
    expect(stub.lastOptions?.linkedTransactionId).toBe(purchaseBillId);
  });
});

class InboxServiceStub {
  constructor(
    private readonly documents: InboxDocument[],
    private readonly fail = false,
  ) {}

  callCount = 0;
  lastOptions: { linkedTransactionType?: string | null; linkedTransactionId?: string | null } | null = null;

  listDocuments(
    _organizationId: string,
    options: { linkedTransactionType?: string | null; linkedTransactionId?: string | null },
  ): Observable<PagedResult<InboxDocument>> {
    this.callCount++;
    this.lastOptions = options;

    return this.fail
      ? throwError(() => new Error('forbidden'))
      : of({ items: this.documents, page: 1, pageSize: 10, totalCount: this.documents.length });
  }

  contentUrl(organizationId: string, id: string): string {
    return `/api/organizations/${organizationId}/workflow/inbox-documents/${id}/content`;
  }
}
