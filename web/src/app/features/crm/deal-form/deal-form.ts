import { Component, OnInit, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { DealStage, LeadSource } from '../../../core/configuration/configuration.models';
import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { CrmService } from '../../../core/crm/crm.service';
import { DealRow } from '../../../core/crm/crm.models';
import { OrganizationMember } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 48 (43 carried item #4) — <b>one Deal form, serving create and edit.</b>
 *
 * <p>The same finding as {@link TaskForm}: phase 43 recorded that editing happened on the list, and
 * the list's form is create-only. `updateDeal` had no caller anywhere in the app.</p>
 *
 * <h4>Contact is shown and not editable, deliberately</h4>
 *
 * <p>The live `Update Deals` modal (read 2026-09-16) <i>does</i> let you change the deal's contact.
 * `UpdateDealCommand` has no `ContactId`, so this form shows the contact as context when editing
 * rather than offering a control that would silently do nothing — phase 43's `WorkspaceName` rule
 * (*a field an aggregate refuses to expose should be absent, not present-and-ignored*), applied to a
 * control rather than to a request field. Re-pointing a deal at a different contact would move its
 * Contact Personnel tab and its place in that contact's Deals list, so it is a modelling decision
 * and not a missing line; it is carried, with the live evidence, rather than added here.</p>
 *
 * <p>Stage and status are not here either, and that matches the live modal: both are left-rail
 * controls driven by `MoveDealToStage` / `MarkDealWon` / `MarkDealLost`, which the list already has.</p>
 */
@Component({
  selector: 'app-deal-form',
  imports: [ReactiveFormsModule, BsDateInput, StatusBanner],
  templateUrl: './deal-form.html',
})
export class DealForm implements OnInit {
  private readonly crmService = inject(CrmService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly contactsService = inject(ContactsService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  readonly organizationId = input.required<string>();

  /** The deal being edited, or null to create a new one. */
  readonly deal = input<DealRow | null>(null);

  /** When set (the Contact detail page's Deals tab), a new deal's contact is implied and hidden. */
  readonly contactId = input<string | null>(null);

  /** See {@link TaskForm.idPrefix} — `id="isPrivate"` was duplicated across both list forms. */
  readonly idPrefix = input<string>('deal-form');

  readonly saved = output<void>();
  readonly cancelled = output<void>();

  protected readonly leadSources = signal<LeadSource[]>([]);
  protected readonly dealStages = signal<DealStage[]>([]);
  protected readonly members = signal<OrganizationMember[]>([]);
  protected readonly dealableContacts = signal<Contact[]>([]);
  protected readonly selectedAssigneeIds = signal<ReadonlySet<string>>(new Set());
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly isEdit = computed(() => this.deal() !== null);

  /** True when the create form must ask for a contact (no implied one, and not editing). */
  protected readonly needsContactPicker = computed(() => !this.isEdit() && !this.contactId());

  protected readonly form = this.fb.nonNullable.group({
    contactId: [''],
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    leadSourceId: [''],
    expectedRevenue: [0, [Validators.required, Validators.min(0)]],
    expectedClosingDate: [''],
    isPrivate: [false],
  });

  constructor() {
    effect(() => {
      const row = this.deal();
      untracked(() => this.reset(row));
    });
  }

  ngOnInit(): void {
    this.configurationService.listLeadSources(this.organizationId()).subscribe({
      next: (sources) => this.leadSources.set(sources),
    });
    this.configurationService.listDealStages(this.organizationId()).subscribe({
      next: (stages) => this.dealStages.set(stages.sort((a, b) => a.sortOrder - b.sortOrder)),
    });
    this.organizationsService.listMembers(this.organizationId()).subscribe({
      next: (members) => this.members.set(members),
    });
    if (this.needsContactPicker()) {
      this.contactsService.listAllContacts(this.organizationId()).subscribe({
        next: (contacts) => this.dealableContacts.set(contacts.filter((c) => c.type !== 'Supplier')),
      });
    }

    // Contact is required only when creating -- the update command does not carry one.
    if (!this.isEdit()) {
      this.form.controls.contactId.addValidators(Validators.required);
      this.form.controls.contactId.updateValueAndValidity();
    }
  }

  /** Re-seeds the form from the record (or to empty). Public so a host can re-open it clean. */
  reset(row: DealRow | null = this.deal()): void {
    this.errorMessage.set(null);
    this.selectedAssigneeIds.set(new Set((row?.assignees ?? []).map((a) => a.userId)));
    this.form.reset({
      contactId: row?.contactId ?? this.contactId() ?? '',
      title: row?.title ?? '',
      description: row?.description ?? '',
      leadSourceId: row?.leadSourceId ?? '',
      expectedRevenue: row?.expectedRevenue ?? 0,
      expectedClosingDate: row?.expectedClosingDate ?? '',
      isPrivate: row?.isPrivate ?? false,
    });
  }

  protected toggleAssignee(userId: string): void {
    this.selectedAssigneeIds.update((current) => {
      const next = new Set(current);
      if (next.has(userId)) {
        next.delete(userId);
      } else {
        next.add(userId);
      }
      return next;
    });
  }

  protected cancel(): void {
    this.cancelled.emit();
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { contactId, title, description, leadSourceId, expectedRevenue, expectedClosingDate, isPrivate } =
      this.form.getRawValue();

    const shared = {
      title,
      assigneeUserIds: [...this.selectedAssigneeIds()],
      leadSourceId: leadSourceId || null,
      description: description || null,
      expectedRevenue,
      expectedClosingDate: expectedClosingDate || null,
      isPrivate,
    };

    const existing = this.deal();

    if (existing) {
      this.crmService.updateDeal(this.organizationId(), existing.id, shared).subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.emit();
        },
        error: (err: unknown) => this.fail(err, 'Could not save the deal. Please try again.'),
      });
    } else {
      this.crmService.createDeal(this.organizationId(), { ...shared, contactId }).subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.emit();
        },
        error: (err: unknown) => this.fail(err, 'Could not create deal. Please try again.'),
      });
    }
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }
}
