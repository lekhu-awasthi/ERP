import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactGroup } from '../../../core/contacts/contacts.models';
import { CrmService } from '../../../core/crm/crm.service';
import { SendSmsRequest, SendSmsResult, SmsAudienceMode, SmsTemplateRowDto } from '../../../core/crm/crm.models';
import { MAX_PAGE_SIZE } from '../../../core/common/paged-result';

/**
 * Shared Send SMS form (roadmap Phase 18) -- reused, not duplicated, across its two integration
 * points: the SMS module's own Overview tab (unlocked, full audience picker) and the Contact
 * detail page's "Send SMS" quick action (lockedContactId set -- audience forced to Custom with
 * that one Contact pre-selected and non-removable, per CLAUDE.md's phase-18 brief -- "don't build
 * a separate Quick SMS component").
 *
 * Merge-field syntax ($[name]$, $[balance]$, $[balance_date]$) resolves on every send here
 * (unlike Tigg's own single-contact-only resolution), so the hint text below doesn't caveat bulk
 * sends.
 */
@Component({
  selector: 'app-send-sms-form',
  imports: [],
  templateUrl: './send-sms-form.html',
})
export class SendSmsForm implements OnInit {
  private readonly crmService = inject(CrmService);
  private readonly contactsService = inject(ContactsService);

  readonly organizationId = input.required<string>();
  readonly lockedContactId = input<string | null>(null);

  readonly sent = output<SendSmsResult>();

  protected readonly sending = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successResult = signal<SendSmsResult | null>(null);

  protected readonly contactGroups = signal<ContactGroup[]>([]);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly templates = signal<SmsTemplateRowDto[]>([]);

  protected readonly audienceModes: SmsAudienceMode[] = ['All', 'ContactGroup', 'Custom'];
  protected readonly audienceMode = signal<SmsAudienceMode>('Custom');
  protected readonly contactGroupId = signal('');
  protected readonly selectedContactIds = signal<Set<string>>(new Set());
  protected readonly templateId = signal('');
  protected readonly title = signal('');
  protected readonly content = signal('');

  protected readonly isLocked = computed(() => !!this.lockedContactId());

  protected readonly lockedContactLabel = computed(() => {
    const id = this.lockedContactId();
    if (!id) {
      return '';
    }
    const contact = this.contacts().find((c) => c.id === id);
    return contact ? `${contact.code} — ${contact.name}` : id;
  });

  ngOnInit(): void {
    this.contactsService.listContactGroups(this.organizationId()).subscribe({ next: (g) => this.contactGroups.set(g) });
    this.contactsService.listAllContacts(this.organizationId()).subscribe({ next: (c) => this.contacts.set(c) });
    this.crmService.listSmsTemplates(this.organizationId(), 1, MAX_PAGE_SIZE).subscribe({
      next: (result) => this.templates.set(result.rows),
    });

    const locked = this.lockedContactId();
    if (locked) {
      this.audienceMode.set('Custom');
      this.selectedContactIds.set(new Set([locked]));
    }
  }

  protected contactLabel(contactId: string): string {
    const contact = this.contacts().find((c) => c.id === contactId);
    return contact ? `${contact.code} — ${contact.name}` : '—';
  }

  protected selectAudienceMode(mode: SmsAudienceMode): void {
    if (this.isLocked()) {
      return;
    }
    this.audienceMode.set(mode);
  }

  protected onContactGroupChange(event: Event): void {
    this.contactGroupId.set((event.target as HTMLSelectElement).value);
  }

  protected toggleContact(contactId: string): void {
    if (this.isLocked()) {
      return;
    }
    const next = new Set(this.selectedContactIds());
    if (next.has(contactId)) {
      next.delete(contactId);
    } else {
      next.add(contactId);
    }
    this.selectedContactIds.set(next);
  }

  protected onTemplateChange(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    this.templateId.set(id);
    const template = this.templates().find((t) => t.id === id);
    if (template) {
      this.content.set(template.content);
    }
  }

  protected onTitleChange(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
  }

  protected onContentChange(event: Event): void {
    this.content.set((event.target as HTMLTextAreaElement).value);
  }

  protected send(): void {
    this.errorMessage.set(null);
    this.successResult.set(null);

    if (!this.title().trim() || !this.content().trim()) {
      this.errorMessage.set('Enter a Title and Content.');
      return;
    }
    if (this.audienceMode() === 'ContactGroup' && !this.contactGroupId()) {
      this.errorMessage.set('Select a Contact Group.');
      return;
    }
    if (this.audienceMode() === 'Custom' && this.selectedContactIds().size === 0) {
      this.errorMessage.set('Select at least one Contact.');
      return;
    }

    const request: SendSmsRequest = {
      audienceMode: this.audienceMode(),
      contactGroupId: this.audienceMode() === 'ContactGroup' ? this.contactGroupId() : null,
      contactIds: this.audienceMode() === 'Custom' ? [...this.selectedContactIds()] : null,
      templateId: this.templateId() || null,
      title: this.title(),
      content: this.content(),
    };

    this.sending.set(true);
    this.crmService.sendSms(this.organizationId(), request).subscribe({
      next: (result) => {
        this.sending.set(false);
        this.successResult.set(result);
        this.title.set('');
        this.content.set('');
        this.templateId.set('');
        this.sent.emit(result);
      },
      error: (err: unknown) => {
        this.sending.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not send SMS. Please try again.');
      },
    });
  }
}
