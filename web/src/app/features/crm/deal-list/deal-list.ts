import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { DealStage, LeadSource } from '../../../core/configuration/configuration.models';
import { Contact } from '../../../core/contacts/contacts.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { CrmService } from '../../../core/crm/crm.service';
import { DealRow, DealStatus } from '../../../core/crm/crm.models';
import { OrganizationMember } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * Shared Deal list component (roadmap Phase 15) -- reused, not duplicated, across its two
 * confirmed live integration points (Contact detail page's Deals tab, Organization dashboard's
 * Deals section), the same "don't duplicate, extract a shared reader" discipline Phase 13's
 * TaskList established. Three status sub-tabs (Pending/Won/Lost, the confirmed live UI shape) plus
 * an inline create form, a per-row inline Stage dropdown, and Won/Lost quick actions.
 *
 * contactId() is optional (unlike TaskList's required parentId) -- when bound (Contact detail
 * page), the create form's Contact is implied and hidden; when omitted (Organization dashboard),
 * the create form shows a Contact picker sourced from Customer/Lead contacts only (Supplier is
 * rejected server-side -- see CrmValidation.EnsureContactCanHaveDealAsync).
 */
@Component({
  selector: 'app-deal-list',
  imports: [ReactiveFormsModule],
  templateUrl: './deal-list.html',
})
export class DealList implements OnInit {
  private readonly crmService = inject(CrmService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly contactsService = inject(ContactsService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  readonly organizationId = input.required<string>();
  readonly contactId = input<string | null>(null);

  protected readonly statuses: DealStatus[] = ['Pending', 'Won', 'Lost'];
  protected readonly activeStatus = signal<DealStatus>('Pending');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<DealRow[]>([]);

  protected readonly leadSources = signal<LeadSource[]>([]);
  protected readonly dealStages = signal<DealStage[]>([]);
  protected readonly members = signal<OrganizationMember[]>([]);
  protected readonly dealableContacts = signal<Contact[]>([]);

  protected readonly showCreateForm = signal(false);
  protected readonly saving = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    contactId: ['', Validators.required],
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    leadSourceId: [''],
    expectedRevenue: [0, [Validators.required, Validators.min(0)]],
    expectedClosingDate: [''],
    isPrivate: [false],
  });

  protected readonly selectedAssigneeIds = signal<Set<string>>(new Set());

  // Reads required inputs, so this runs from ngOnInit (guaranteed after Angular has bound the
  // inputs), not the constructor (NG8118 -- an input() isn't readable that early).
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
    if (!this.contactId()) {
      this.contactsService.listContacts(this.organizationId()).subscribe({
        next: (contacts) => this.dealableContacts.set(contacts.filter((c) => c.type !== 'Supplier')),
      });
    }
    this.load();
  }

  protected switchTab(status: DealStatus): void {
    this.activeStatus.set(status);
    this.load();
  }

  protected toggleCreateForm(): void {
    this.showCreateForm.set(!this.showCreateForm());
    this.errorMessage.set(null);
    this.selectedAssigneeIds.set(new Set());
    this.form.reset({
      contactId: this.contactId() ?? '',
      title: '',
      description: '',
      leadSourceId: '',
      expectedRevenue: 0,
      expectedClosingDate: '',
      isPrivate: false,
    });
  }

  protected toggleAssignee(userId: string): void {
    const next = new Set(this.selectedAssigneeIds());
    if (next.has(userId)) {
      next.delete(userId);
    } else {
      next.add(userId);
    }
    this.selectedAssigneeIds.set(next);
  }

  protected submitCreate(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { contactId, title, description, leadSourceId, expectedRevenue, expectedClosingDate, isPrivate } =
      this.form.getRawValue();

    this.crmService
      .createDeal(this.organizationId(), {
        contactId,
        title,
        assigneeUserIds: [...this.selectedAssigneeIds()],
        leadSourceId: leadSourceId || null,
        description: description || null,
        expectedRevenue,
        expectedClosingDate: expectedClosingDate || null,
        isPrivate,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.showCreateForm.set(false);
          this.activeStatus.set('Pending');
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create deal. Please try again.');
        },
      });
  }

  protected changeStage(row: DealRow, event: Event): void {
    const dealStageId = (event.target as HTMLSelectElement).value;
    if (!dealStageId) {
      return;
    }
    this.crmService.moveDealToStage(this.organizationId(), row.id, { dealStageId }).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not move the deal. Please try again.');
      },
    });
  }

  protected markWon(row: DealRow): void {
    this.crmService.markDealWon(this.organizationId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not mark the deal Won. Please try again.');
      },
    });
  }

  protected markLost(row: DealRow): void {
    this.crmService.markDealLost(this.organizationId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not mark the deal Lost. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.crmService.listDeals(this.organizationId(), this.contactId(), this.activeStatus()).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load deals.');
      },
    });
  }
}
