import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CommunicationsService } from '../../../core/communications/communications.service';
import {
  EmailMergeField,
  EmailTemplateContext,
  EmailTemplateContextOption,
  EmailTemplateDto,
} from '../../../core/communications/communications.models';

interface EmailTemplateSection {
  context: EmailTemplateContext;
  title: string;
  items: EmailTemplateDto[];
}

/**
 * Phase 30 -- Configurations > Email Templates (FR-11.1/11.3).
 *
 * <p>Its own page rather than a fifth section on Custom Templates, because an email template is a
 * different aggregate: it carries a Subject, a Reply-To and default CC/BCC lists, and its type
 * vocabulary is the <i>document</i> it is written for rather than one of four kinds of letter. See
 * docs/phase-30-status.md Decision B, and note the reference product itself serves the two from
 * different resources despite showing them in one panel.</p>
 *
 * <p><b>Context is set at creation and disabled on edit</b>, matching the live form and enforcing a
 * real invariant: a body written against `$[INVOICE_NO]$` renders raw placeholders the moment it is
 * moved to another context.</p>
 *
 * <p>The merge-field catalogue is served by the API rather than hard-coded here, so the tokens this
 * screen offers cannot drift from the ones the resolver actually substitutes.</p>
 */
@Component({
  selector: 'app-email-template-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './email-template-list-page.html',
})
export class EmailTemplateListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly communicationsService = inject(CommunicationsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<EmailTemplateDto[]>([]);
  protected readonly contexts = signal<EmailTemplateContextOption[]>([]);
  protected readonly mergeFields = signal<EmailMergeField[]>([]);

  protected readonly editingId = signal<string | null>(null);
  protected readonly formOpen = signal(false);

  /** The context the open form is for. Held separately from the form because it is disabled on
   * edit, and a disabled control's value is excluded from `form.value`. */
  protected readonly formContext = signal<EmailTemplateContext>('Invoice');

  protected readonly sections = computed<readonly EmailTemplateSection[]>(() =>
    this.contexts().map((context) => ({
      context: context.context,
      title: context.name,
      items: this.items().filter((item) => item.context === context.context),
    })),
  );

  /** Grouped for display, in the catalogue's own menu order. */
  protected readonly mergeFieldGroups = computed(() => {
    const groups = new Map<string, EmailMergeField[]>();
    for (const field of this.mergeFields()) {
      const existing = groups.get(field.group);
      if (existing) {
        existing.push(field);
      } else {
        groups.set(field.group, [field]);
      }
    }
    return Array.from(groups, ([group, fields]) => ({ group, fields }));
  });

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    subject: ['', [Validators.required, Validators.maxLength(500)]],
    body: ['', [Validators.required]],
    replyTo: [''],
    cc: [''],
    bcc: [''],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  protected startCreate(context: EmailTemplateContext): void {
    this.editingId.set(null);
    this.formContext.set(context);
    this.form.reset({ name: '', subject: '', body: '', replyTo: '', cc: '', bcc: '', isActive: true });
    this.formOpen.set(true);
    this.errorMessage.set(null);
  }

  protected startEdit(template: EmailTemplateDto): void {
    this.editingId.set(template.id);
    this.formContext.set(template.context);
    this.form.reset({
      name: template.name,
      subject: template.subject,
      body: template.body,
      replyTo: template.replyTo ?? '',
      cc: template.cc ?? '',
      bcc: template.bcc ?? '',
      isActive: template.isActive,
    });
    this.formOpen.set(true);
    this.errorMessage.set(null);
  }

  protected cancel(): void {
    this.formOpen.set(false);
    this.editingId.set(null);
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const raw = this.form.getRawValue();
    const id = this.editingId();

    // Explicit if/else rather than a shared request$ variable: the two results are different types
    // and sharing one silently widens both (phase-4 bug #3).
    if (id) {
      this.communicationsService
        .updateEmailTemplate(this.organizationId, id, {
          name: raw.name,
          subject: raw.subject,
          body: raw.body,
          replyTo: raw.replyTo || null,
          cc: raw.cc || null,
          bcc: raw.bcc || null,
          isActive: raw.isActive,
        })
        .subscribe({ next: () => this.onSaved(), error: (err: unknown) => this.onError(err) });
    } else {
      this.communicationsService
        .createEmailTemplate(this.organizationId, {
          name: raw.name,
          context: this.formContext(),
          subject: raw.subject,
          body: raw.body,
          replyTo: raw.replyTo || null,
          cc: raw.cc || null,
          bcc: raw.bcc || null,
        })
        .subscribe({ next: () => this.onSaved(), error: (err: unknown) => this.onError(err) });
    }
  }

  protected setDefault(template: EmailTemplateDto): void {
    this.communicationsService.setDefaultEmailTemplate(this.organizationId, template.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => this.onError(err),
    });
  }

  private onSaved(): void {
    this.saving.set(false);
    this.formOpen.set(false);
    this.editingId.set(null);
    this.load();
  }

  private onError(err: unknown): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save the template.');
  }

  private load(): void {
    this.loading.set(true);
    this.communicationsService.listEmailTemplates(this.organizationId, null, true).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.items.set(result.templates);
        this.contexts.set(result.contexts);
        this.mergeFields.set(result.mergeFields);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load email templates.');
      },
    });
  }
}
