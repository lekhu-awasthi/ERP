import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { CommunicationsService } from '../../core/communications/communications.service';
import { PreparedEmail, SendEmailRequest } from '../../core/communications/communications.models';
import { SendEmailDialog } from './send-email-dialog';

function draft(overrides: Partial<PreparedEmail> = {}): PreparedEmail {
  return {
    context: 'Invoice',
    contextName: 'Invoice',
    templates: [{ id: 't1', name: 'Invoice Notification', isDefault: true }],
    defaultTemplateId: 't1',
    subject: 'Invoice From Moonbeam Trading',
    body: 'Hello Adhitya Bhandari,',
    replyTo: 'sales@example.test',
    cc: [],
    bcc: [],
    suggestedTo: ['adhitya@example.test', 'accounts@example.test'],
    canAttachDocumentPdf: true,
    documentCode: '045',
    unresolvedTokens: [],
    ...overrides,
  };
}

describe('SendEmailDialog', () => {
  let fixture: ComponentFixture<SendEmailDialog>;
  let component: SendEmailDialog;
  let sent: SendEmailRequest[];
  let prepared: PreparedEmail;
  let failSend: boolean;

  beforeEach(async () => {
    sent = [];
    prepared = draft();
    failSend = false;

    const service = {
      prepareEmail: () => of(prepared),
      sendEmail: (_organizationId: string, request: SendEmailRequest) => {
        if (failSend) {
          return throwError(() => new Error('nope'));
        }
        sent.push(request);
        return of({ emailSendLogId: 'log-1', alreadyQueued: false });
      },
    };

    await TestBed.configureTestingModule({
      imports: [SendEmailDialog],
      providers: [
        provideZonelessChangeDetection(),
        { provide: CommunicationsService, useValue: service },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SendEmailDialog);
    fixture.componentRef.setInput('organizationId', 'org-1');
    fixture.componentRef.setInput('documentType', 'Invoice');
    fixture.componentRef.setInput('parentId', 'inv-1');
    component = fixture.componentInstance;
  });

  function open(): void {
    component.show();
    fixture.detectChanges();
  }

  it('is closed until show() is called', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('New Email');
  });

  it('opens on the resolved draft, with merge fields already substituted', () => {
    open();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('New Email');

    const subject = fixture.nativeElement.querySelector('#send-email-subject') as HTMLInputElement;
    expect(subject.value).toBe('Invoice From Moonbeam Trading');

    const replyTo = fixture.nativeElement.querySelector('#send-email-reply-to') as HTMLInputElement;
    expect(replyTo.value).toBe('sales@example.test');
  });

  /**
   * Live behaviour: To pre-fills with the contact's own address when there is one, and the rest are
   * offered behind "More..." rather than added silently -- a CC'd stranger on an invoice is a real
   * mistake, so the second address is a choice.
   */
  it('pre-fills only the first suggested recipient and offers the rest behind More', () => {
    open();
    expect(fixture.nativeElement.textContent).toContain('adhitya@example.test');
    expect(sentRecipients()).toEqual(['adhitya@example.test']);
  });

  it('sends what the composer sees, with the attach-PDF checkbox on by default', () => {
    open();
    component['send']();

    expect(sent).toHaveLength(1);
    expect(sent[0].subject).toBe('Invoice From Moonbeam Trading');
    expect(sent[0].body).toBe('Hello Adhitya Bhandari,');
    expect(sent[0].attachDocumentPdf).toBe(true);
    expect(sent[0].documentType).toBe('Invoice');
    expect(sent[0].parentId).toBe('inv-1');
  });

  /**
   * The idempotency contract, from the client's side: one opened dialog means one request id, and
   * reopening mints a fresh one so a deliberate resend is a new row. See EmailSendLog.
   */
  it('mints one request id per opened dialog and a fresh one on reopen', () => {
    open();
    component['send']();

    open();
    component['send']();

    expect(sent).toHaveLength(2);
    expect(sent[0].requestId).not.toBe(sent[1].requestId);
    expect(sent[0].requestId).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('refuses to send without a recipient', () => {
    prepared = draft({ suggestedTo: [] });
    open();

    component['send']();
    expect(sent).toHaveLength(0);
  });

  it('adds a pasted list of recipients on either separator, without duplicates', () => {
    prepared = draft({ suggestedTo: [] });
    open();

    component['toDraft'].set('a@x.test, b@x.test; A@X.test');
    component['commitToDraft']();

    expect(component['to']()).toEqual(['a@x.test', 'b@x.test']);
  });

  it('warns about placeholders the server could not fill in', () => {
    prepared = draft({ unresolvedTokens: ['TOATL'] });
    open();

    expect(fixture.nativeElement.textContent).toContain('TOATL');
  });

  /** A Contact-scoped send has no document, so no checkbox -- live, the Contact dialog has none. */
  it('hides the attach-PDF checkbox when there is no document', () => {
    fixture.componentRef.setInput('documentType', null);
    prepared = draft({ canAttachDocumentPdf: false, contextName: 'General' });
    open();

    expect(fixture.nativeElement.querySelector('#send-email-attach-pdf')).toBeNull();
  });

  it('surfaces a failed send and stays open so the message is not lost', () => {
    open();
    failSend = true;

    component['send']();
    fixture.detectChanges();

    expect(sent).toHaveLength(0);
    expect(fixture.nativeElement.textContent).toContain('New Email');
    expect(component['errorMessage']()).not.toBeNull();
  });

  function sentRecipients(): string[] {
    return component['to']();
  }
});
