import { DecimalPipe } from '@angular/common';
import { Component, inject, input } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

import { InboxPrefill } from '../../core/workflow/inbox.models';
import { InboxService } from '../../core/workflow/inbox.service';

/**
 * Phase 22 (FR-10.3) -- the side-by-side pane the roadmap asks for: while converting an inbox
 * document, the target form shows the scan it is being typed from.
 *
 * <p>It also carries <b>the honesty requirement</b> (docs/phase-22-status.md, Decision C). When the
 * pre-fill came from extraction it says so in plain words, lists exactly which values a machine
 * suggested, and names the model. Nothing here is subtle: a number an LLM guessed at is about to be
 * approved into the General Ledger by a human, and the screen's job is to make sure that human knows
 * which numbers those are.</p>
 *
 * <p>When there was no extraction (the common case -- a manual conversion), the banner is absent
 * entirely and this is just a preview of the file. Claiming AI involvement where there was none
 * would be its own kind of dishonesty.</p>
 */
@Component({
  selector: 'app-inbox-conversion-panel',
  imports: [DecimalPipe],
  templateUrl: './inbox-conversion-panel.html',
})
export class InboxConversionPanel {
  private readonly inboxService = inject(InboxService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly organizationId = input.required<string>();
  readonly prefill = input.required<InboxPrefill>();

  protected previewUrl(): string {
    return this.inboxService.contentUrl(this.organizationId(), this.prefill().documentId);
  }

  protected previewResourceUrl(): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(this.previewUrl());
  }

  protected isImage(): boolean {
    return /\.(png|jpe?g|gif|webp)$/i.test(this.prefill().fileName);
  }

  protected isPdf(): boolean {
    return /\.pdf$/i.test(this.prefill().fileName);
  }
}
