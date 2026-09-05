import { Component, computed, inject, input, output, signal } from '@angular/core';

import { extractErrorMessage } from '../../core/auth/api-error';
import { CommunicationsService } from '../../core/communications/communications.service';
import { PreparedEmail } from '../../core/communications/communications.models';
import { DocumentType } from '../../core/sales/sales.models';

/**
 * Phase 30 -- the Send Email dialog (FR-11.1).
 *
 * <p><b>Shape confirmed live</b> on the reference tenant's Invoice detail (2026-09-05,
 * docs/phase-30-status.md Step 1.4): Template*, To* with a "More..." picker over the contact's known
 * addresses plus CC/BCC, Reply To* defaulting to the signed-in user, Subject*, a body editor, an
 * "Attach &lt;Document&gt; PDF" checkbox that is <i>on</i> by default, and a drop zone for extra
 * files.</p>
 *
 * <p><b>The subject and body arrive already substituted.</b> That is live behaviour and it is the
 * decision the whole feature turns on: what the user edits and sends is the document's own text,
 * seeded from a template, never a template reference resolved later on a server after the sender has
 * stopped looking. Phase 27b reached the same conclusion for Terms and Conditions.</p>
 *
 * <p><b>One deliberate divergence</b>, and the same one `app-terms-editor` made: the reference
 * editor is TinyMCE, this is a textarea. The body is sent as HTML either way, so the seam to upgrade
 * is this one component -- and a plain textarea cannot silently inject markup into a customer-facing
 * message, which on this particular screen is a feature.</p>
 *
 * <p><b>`requestId` is minted once, when the dialog opens</b>, and is the idempotency key: a
 * double-clicked Send resolves to one email, while reopening the dialog mints a fresh one so a
 * deliberate resend is a new row. See EmailSendLog for the full argument.</p>
 *
 * <p>Rendered as a plain fixed-position overlay rather than a Bootstrap modal: Bootstrap's
 * JavaScript is not loaded anywhere in this app (angular.json has no `scripts`), so `data-bs-*`
 * attributes do nothing (phase-22's gotcha).</p>
 */
@Component({
  selector: 'app-send-email-dialog',
  imports: [],
  templateUrl: './send-email-dialog.html',
  styleUrl: './send-email-dialog.scss',
})
export class SendEmailDialog {
  private readonly communicationsService = inject(CommunicationsService);

  readonly organizationId = input.required<string>();

  /** Null for a Contact-scoped send -- the Contact detail page's own action. */
  readonly documentType = input<DocumentType | null>(null);
  readonly parentId = input.required<string>();

  /** Raised once the send has been accepted, so a host can refresh its Email Logs tab. */
  readonly sent = output<void>();
  readonly closed = output<void>();

  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly sending = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly prepared = signal<PreparedEmail | null>(null);

  // The app is zoneless: a computed() over a plain FormControl caches forever, so every UI-driving
  // value is its own signal written by the control's own event handler (phase-17's gotcha).
  protected readonly templateId = signal<string | null>(null);
  protected readonly to = signal<string[]>([]);
  protected readonly cc = signal<string[]>([]);
  protected readonly bcc = signal<string[]>([]);
  protected readonly replyTo = signal('');
  protected readonly subject = signal('');
  protected readonly body = signal('');
  protected readonly attachPdf = signal(true);
  protected readonly files = signal<File[]>([]);

  protected readonly showCc = signal(false);
  protected readonly showBcc = signal(false);
  protected readonly showSuggestions = signal(false);
  protected readonly toDraft = signal('');

  private requestId = '';

  protected readonly attachLabel = computed(() => {
    const type = this.documentType();
    return type ? `Attach ${this.prepared()?.contextName ?? type} PDF` : '';
  });

  protected readonly canSend = computed(
    () => this.to().length > 0 && this.subject().trim().length > 0 && this.body().trim().length > 0,
  );

  /** Addresses offered by the live "More..." picker that are not already in To. */
  protected readonly suggestions = computed(() => {
    const chosen = new Set(this.to().map((x) => x.toLowerCase()));
    return (this.prepared()?.suggestedTo ?? []).filter((x) => !chosen.has(x.toLowerCase()));
  });

  show(): void {
    // A fresh id per opened dialog: this is what makes a double-click one email and a deliberate
    // reopen-and-resend a genuinely new one.
    this.requestId = crypto.randomUUID();

    this.open.set(true);
    this.loading.set(true);
    this.errorMessage.set(null);
    this.prepared.set(null);
    this.files.set([]);
    this.showCc.set(false);
    this.showBcc.set(false);
    this.showSuggestions.set(false);
    this.toDraft.set('');

    this.communicationsService
      .prepareEmail(this.organizationId(), this.documentType(), this.parentId())
      .subscribe({
        next: (prepared) => {
          this.loading.set(false);
          this.prepared.set(prepared);
          this.templateId.set(prepared.defaultTemplateId);
          this.subject.set(prepared.subject);
          this.body.set(prepared.body);
          this.replyTo.set(prepared.replyTo ?? '');
          this.cc.set(prepared.cc);
          this.bcc.set(prepared.bcc);
          this.showCc.set(prepared.cc.length > 0);
          this.showBcc.set(prepared.bcc.length > 0);
          this.attachPdf.set(prepared.canAttachDocumentPdf);

          // Live, To is empty when the contact has no address on file and the picker reads
          // "No data found" -- so this pre-fills only what actually exists.
          this.to.set(prepared.suggestedTo.slice(0, 1));
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not open the email composer.');
        },
      });
  }

  protected close(): void {
    this.open.set(false);
    this.closed.emit();
  }

  /** Switching template re-resolves the draft server-side, so the preview stays authoritative. */
  protected onTemplateChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value || null;
    this.templateId.set(id);

    const template = this.prepared()?.templates.find((x) => x.id === id);
    if (!template) {
      return;
    }

    this.loading.set(true);
    this.communicationsService
      .prepareEmail(this.organizationId(), this.documentType(), this.parentId())
      .subscribe({
        next: (prepared) => {
          this.loading.set(false);
          this.prepared.set(prepared);
          this.subject.set(prepared.subject);
          this.body.set(prepared.body);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load that template.');
        },
      });
  }

  protected onToDraftInput(event: Event): void {
    this.toDraft.set((event.target as HTMLInputElement).value);
  }

  protected commitToDraft(): void {
    const raw = this.toDraft().trim();
    if (!raw) {
      return;
    }

    // Accept a pasted list on either separator, the way a mail client does.
    const added = raw
      .split(/[,;]/)
      .map((x) => x.trim())
      .filter((x) => x.length > 0);

    this.addRecipients(added);
    this.toDraft.set('');
  }

  protected addSuggestion(address: string): void {
    this.addRecipients([address]);
  }

  protected removeRecipient(address: string): void {
    this.to.update((current) => current.filter((x) => x !== address));
  }

  protected onCcInput(event: Event): void {
    this.cc.set(this.splitAddresses((event.target as HTMLInputElement).value));
  }

  protected onBccInput(event: Event): void {
    this.bcc.set(this.splitAddresses((event.target as HTMLInputElement).value));
  }

  protected onReplyToInput(event: Event): void {
    this.replyTo.set((event.target as HTMLInputElement).value);
  }

  protected onSubjectInput(event: Event): void {
    this.subject.set((event.target as HTMLInputElement).value);
  }

  protected onBodyInput(event: Event): void {
    this.body.set((event.target as HTMLTextAreaElement).value);
  }

  protected onAttachPdfChange(event: Event): void {
    this.attachPdf.set((event.target as HTMLInputElement).checked);
  }

  protected onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.files.update((current) => [...current, ...Array.from(input.files ?? [])]);

    // Clear the control so re-picking the same file raises another change event.
    input.value = '';
  }

  protected removeFile(file: File): void {
    this.files.update((current) => current.filter((x) => x !== file));
  }

  protected send(): void {
    if (!this.canSend() || this.sending()) {
      return;
    }

    this.sending.set(true);
    this.errorMessage.set(null);

    this.communicationsService
      .sendEmail(this.organizationId(), {
        requestId: this.requestId,
        documentType: this.documentType(),
        parentId: this.parentId(),
        templateId: this.templateId(),
        to: this.to(),
        cc: this.cc(),
        bcc: this.bcc(),
        replyTo: this.replyTo().trim() || null,
        subject: this.subject(),
        body: this.body(),
        attachDocumentPdf: this.attachPdf() && this.documentType() !== null,
        files: this.files(),
      })
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.open.set(false);

          // Emitted for both the accepted and already-queued outcomes: from the user's side they
          // are the same event, and the log is where the difference (if any) shows up.
          this.sent.emit();
        },
        error: (err: unknown) => {
          this.sending.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not send the email.');
        },
      });
  }

  private addRecipients(addresses: string[]): void {
    this.to.update((current) => {
      const seen = new Set(current.map((x) => x.toLowerCase()));
      const next = [...current];

      for (const address of addresses) {
        if (!seen.has(address.toLowerCase())) {
          seen.add(address.toLowerCase());
          next.push(address);
        }
      }

      return next;
    });
  }

  private splitAddresses(raw: string): string[] {
    return raw
      .split(/[,;]/)
      .map((x) => x.trim())
      .filter((x) => x.length > 0);
  }
}
