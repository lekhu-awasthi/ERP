import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { AiDocumentExtractionSetting, InboxDocument } from '../../../core/workflow/inbox.models';
import { InboxService } from '../../../core/workflow/inbox.service';
import { DocumentInboxPage } from './document-inbox-page';

/**
 * Phase 22's inbox grid. A rendering test rather than a browser pass, because what matters here is
 * <b>what the screen is allowed to offer and what it is obliged to say</b>:
 *
 * <ul>
 *   <li>"+ Add as" must key off <code>isLinked</code>, never <code>status</code> -- a document
 *       already converted must not offer a second conversion the server would then refuse;</li>
 *   <li>an extraction result must be labelled as a suggestion, not shown as fact -- the honesty
 *       requirement is a UI promise, and a screenshot would not catch it going missing;</li>
 *   <li>the consent card must state plainly what leaves the tenant, before anyone clicks.</li>
 * </ul>
 */
describe('DocumentInboxPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function document(overrides: Partial<InboxDocument> = {}): InboxDocument {
    return {
      id: 'doc-1',
      fileName: 'supplier-bill.pdf',
      sizeBytes: 20480,
      contentType: 'application/pdf',
      description: null,
      label: null,
      status: 'Pending',
      uploadedByUserId: 'user-1',
      uploadedByName: 'Ram Bahadur',
      uploadedAt: '2026-09-01T07:39:46Z',
      isLinked: false,
      linkedTransactionType: null,
      linkedTransactionId: null,
      linkedAt: null,
      extractionStatus: 'NotAttempted',
      extractionModelId: null,
      extractionFailureReason: null,
      extractionAttemptedAt: null,
      isExtractable: true,
      extractedData: null,
      ...overrides,
    };
  }

  function page(
    documents: InboxDocument[],
    setting: AiDocumentExtractionSetting = { enabled: false, extractorConfigured: false, modelId: null },
  ): { fixture: ComponentFixture<DocumentInboxPage>; text: () => string; element: () => HTMLElement } {
    TestBed.configureTestingModule({
      imports: [DocumentInboxPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InboxService, useValue: new InboxServiceStub(documents, setting) },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(DocumentInboxPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    return { fixture, element, text: () => element().textContent ?? '' };
  }

  it('offers "+ Add as" for an unlinked document', () => {
    const { text } = page([document()]);
    expect(text()).toContain('+ Add as');
  });

  it('does not offer "+ Add as" for a document that already produced a transaction', () => {
    const { text } = page([
      document({
        isLinked: true,
        status: 'Done',
        linkedTransactionType: 'PurchaseBill',
        linkedTransactionId: 'bill-1',
        linkedAt: '2026-09-01T08:00:00Z',
      }),
    ]);

    expect(text()).not.toContain('+ Add as');
    expect(text()).toContain('Purchase Bill');
  });

  it('offers no Delete or Reopen for a linked document, since the server refuses both', () => {
    const { element } = page([
      document({
        isLinked: true,
        status: 'Done',
        linkedTransactionType: 'Invoice',
        linkedTransactionId: 'inv-1',
      }),
    ]);

    expect(element().querySelector('button[title="Delete"]')).toBeNull();
    expect(element().querySelector('button[title="Move back to Pending"]')).toBeNull();
  });

  it('labels an extraction result as a suggestion to check, never as data', () => {
    const { text } = page(
      [
        document({
          extractionStatus: 'Succeeded',
          extractionModelId: 'claude-opus-5',
          extractionAttemptedAt: '2026-09-01T07:45:00Z',
          extractedData: {
            partyName: 'Global Supplies',
            partyPan: '301234567',
            documentDate: '2026-04-17',
            reference: 'INV-4471',
            totalAmount: 1130,
            vatAmount: 130,
            lines: [],
          },
        }),
      ],
      { enabled: true, extractorConfigured: true, modelId: 'claude-opus-5' },
    );

    expect(text()).toContain('check every value before you save');
    expect(text()).toContain('Global Supplies');
    expect(text()).toContain('Discard these values');
  });

  it('says a failed extraction is not an error and leaves the document convertible', () => {
    const { text } = page([
      document({
        extractionStatus: 'Failed',
        extractionFailureReason: 'Extraction timed out after 90 seconds.',
      }),
    ]);

    expect(text()).toContain('Nothing was read from this document');
    expect(text()).toContain('Extraction timed out after 90 seconds.');
    // The conversion is still on offer -- that is the whole point of a failure being an outcome.
    expect(text()).toContain('+ Add as');
  });

  it('states what leaves the tenant on the consent card, before anyone turns it on', () => {
    const { text } = page([document()], { enabled: false, extractorConfigured: true, modelId: 'claude-opus-5' });

    expect(text()).toContain('sends');
    expect(text()).toContain('that one file');
    expect(text()).toContain('no contact list, no product catalogue, no other document');
    expect(text()).toContain('claude-opus-5');
  });

  it('warns when the tenant has opted in but the server has no credential', () => {
    const { text } = page([document()], { enabled: true, extractorConfigured: false, modelId: 'claude-opus-5' });
    expect(text()).toContain('no extraction credential configured');
  });

  it('does not offer Extract while the tenant has not opted in', () => {
    const { text } = page([document()], { enabled: false, extractorConfigured: true, modelId: 'claude-opus-5' });

    expect(text()).not.toContain('Extract fields with AI');
    expect(text()).toContain('turned off for this organization');
  });

  it('says a non-extractable file is still convertible by hand', () => {
    const { text } = page(
      [document({ fileName: 'quote.xlsx', isExtractable: false })],
      { enabled: true, extractorConfigured: true, modelId: 'claude-opus-5' },
    );

    expect(text()).toContain('Extraction only works on images and PDFs');
    expect(text()).toContain('+ Add as');
  });
});

class InboxServiceStub {
  constructor(
    private readonly documents: InboxDocument[],
    private readonly setting: AiDocumentExtractionSetting,
  ) {}

  listDocuments(): Observable<PagedResult<InboxDocument>> {
    return of({ items: this.documents, page: 1, pageSize: 25, totalCount: this.documents.length });
  }

  getExtractionSetting(): Observable<AiDocumentExtractionSetting> {
    return of(this.setting);
  }

  contentUrl(organizationId: string, id: string): string {
    return `/api/organizations/${organizationId}/workflow/inbox-documents/${id}/content`;
  }

  downloadUrl(organizationId: string, id: string): string {
    return `/api/organizations/${organizationId}/workflow/inbox-documents/${id}/download`;
  }
}
