import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { buildTreeRows, TreeRow } from '../../../core/common/tree';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact, ContactGroup, ContactOverviewDto, ContactType } from '../../../core/contacts/contacts.models';
import { DealList } from '../../crm/deal-list/deal-list';
import { SendSmsForm } from '../../crm/send-sms-form/send-sms-form';
import { TaskList } from '../../workflow/task-list/task-list';
import { ContactPersonnelList } from '../contact-personnel-list/contact-personnel-list';
import { AttachmentList } from '../attachment-list/attachment-list';
import { ActivityPanel } from '../activity-panel/activity-panel';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/** Record-detail-page chrome: left mini-profile panel + vertical tab list + right content pane
 * -- new pattern for this codebase, established here per roadmap Phase 3's Angular deliverable.
 * Only the Overview tab is built; the Activity tab (Comments/Activities/SMS/Email Logs) is
 * explicitly out of scope for Phase 3 (Workflow bounded context, Phase 8+). One component
 * handles both 'new' and ':contactId' routes to avoid a near-duplicate create-only component.
 *
 * Subscribes to route.paramMap (not route.snapshot) -- Angular's default route reuse strategy
 * keeps this same component instance alive when navigating contacts/new -> contacts/:contactId
 * (both match this route's path), so a snapshot captured once in the constructor would go stale
 * right after Create redirects to the new record's own URL.
 *
 * activeTab (Phase 13) is the first real tab-switching signal this page has ever had -- until now
 * the vertical tab list was a single hardcoded always-active "Overview" button with no click
 * handler at all. Phase 15 adds a Deals tab alongside Tasks -- hidden for a Supplier Contact (a
 * Deal is a pre-sale/sales-pipeline concept, see CrmValidation.EnsureContactCanHaveDealAsync's own
 * doc comment for why Supplier is rejected server-side too). */
@Component({
  selector: 'app-contact-detail-page',
  imports: [ReactiveFormsModule,
    RouterLink,
    TaskList,
    DealList,
    ContactPersonnelList,
    AttachmentList,
    ActivityPanel,
    SendSmsForm, AmountPipe, NepaliDatePipe],
  templateUrl: './contact-detail-page.html',
})
export class ContactDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly contactsService = inject(ContactsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly contact = signal<Contact | null>(null);
  protected readonly editing = signal(false);
  protected readonly optionsOpen = signal(false);
  protected readonly groups = signal<ContactGroup[]>([]);
  protected readonly isNew = signal(false);
  protected readonly activeTab = signal<'Overview' | 'Personnel' | 'Tasks' | 'Deals' | 'Documents' | 'Activity'>('Overview');
  protected readonly showSendSms = signal(false);

  protected readonly overview = signal<ContactOverviewDto | null>(null);
  protected readonly overviewLoading = signal(false);
  protected readonly overviewError = signal<string | null>(null);

  protected routeContactId = '';

  protected readonly groupRows = computed<TreeRow<ContactGroup>[]>(() =>
    buildTreeRows(
      this.groups(),
      (group) => group.id,
      (group) => group.parentGroupId,
      (group) => group.name,
    ),
  );

  protected readonly types: ContactType[] = ['Customer', 'Supplier', 'Lead'];

  protected readonly form = this.fb.nonNullable.group({
    type: ['Customer' as ContactType, Validators.required],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    address: [''],
    pan: [''],
    phone: [''],
    email: [''],
    groupId: [''],
    openingBalance: [0, [Validators.required, Validators.min(0)]],
  });

  constructor() {
    this.contactsService.listContactGroups(this.organizationId).subscribe({
      next: (groups) => this.groups.set(groups),
    });

    this.route.paramMap.subscribe((params) => {
      this.routeContactId = params.get('contactId')!;
      const isNew = this.routeContactId === 'new';
      this.isNew.set(isNew);
      this.editing.set(isNew);
      this.contact.set(null);
      this.errorMessage.set(null);
      this.optionsOpen.set(false);
      this.overview.set(null);
      this.overviewError.set(null);
      this.activeTab.set('Overview');
      this.showSendSms.set(false);

      if (isNew) {
        this.loading.set(false);
        this.form.reset({
          type: 'Customer',
          name: '',
          address: '',
          pan: '',
          phone: '',
          email: '',
          groupId: '',
          openingBalance: 0,
        });
      } else {
        this.load();
      }
    });
  }

  protected switchTab(tab: 'Overview' | 'Personnel' | 'Tasks' | 'Deals' | 'Documents' | 'Activity'): void {
    this.activeTab.set(tab);
  }

  // --- OPTION menu quick actions (FR-4.6, roadmap Phase 18) -- routing + prefill only, no new
  // commands. Each target reads route.queryParamMap reactively for ?contactId= (see that page's
  // own doc comment for the route-reuse-safe implementation). ---

  protected sendSms(): void {
    this.optionsOpen.set(false);
    this.showSendSms.set(true);
  }

  protected closeSendSms(): void {
    this.showSendSms.set(false);
  }

  /** Tigg's own "Record Payment" opens a plain Payment create form pre-filled with the Contact --
   * this codebase splits that by Direction into two components sharing the same Payment aggregate
   * (payment-detail-page for Customer/Received, supplier-payment-detail-page for Supplier/Paid),
   * so route by the Contact's own type rather than always targeting the Customer variant. */
  protected recordPayment(): void {
    this.optionsOpen.set(false);
    const contact = this.contact();
    if (contact?.type === 'Supplier') {
      this.router.navigate(['/organizations', this.organizationId, 'purchasing', 'supplier-payments', 'new'], {
        queryParams: { contactId: this.routeContactId },
      });
    } else {
      this.router.navigate(['/organizations', this.organizationId, 'payments', 'new'], {
        queryParams: { contactId: this.routeContactId },
      });
    }
  }

  protected createInvoice(): void {
    this.optionsOpen.set(false);
    this.router.navigate(['/organizations', this.organizationId, 'sales', 'invoices', 'new'], {
      queryParams: { contactId: this.routeContactId },
    });
  }

  protected createQuotation(): void {
    this.optionsOpen.set(false);
    this.router.navigate(['/organizations', this.organizationId, 'sales', 'quotations', 'new'], {
      queryParams: { contactId: this.routeContactId },
    });
  }

  protected createSalesOrder(): void {
    this.optionsOpen.set(false);
    this.router.navigate(['/organizations', this.organizationId, 'sales', 'sales-orders', 'new'], {
      queryParams: { contactId: this.routeContactId },
    });
  }

  protected startEdit(): void {
    const contact = this.contact();
    if (contact) {
      this.form.reset({
        type: contact.type,
        name: contact.name,
        address: contact.address ?? '',
        pan: contact.pan ?? '',
        phone: contact.phone ?? '',
        email: contact.email ?? '',
        groupId: contact.groupId ?? '',
        openingBalance: contact.openingBalance,
      });
    }
    this.editing.set(true);
    this.optionsOpen.set(false);
  }

  protected cancelEdit(): void {
    if (this.isNew()) {
      this.router.navigate(['/organizations', this.organizationId, 'contacts']);
      return;
    }
    this.editing.set(false);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { type, name, address, pan, phone, email, groupId, openingBalance } = this.form.getRawValue();
    const groupIdOrNull = groupId || null;
    const addressOrNull = address || null;
    const panOrNull = pan || null;
    const phoneOrNull = phone || null;
    const emailOrNull = email || null;

    if (this.isNew()) {
      this.contactsService
        .createContact(this.organizationId, {
          type,
          name,
          address: addressOrNull,
          pan: panOrNull,
          phone: phoneOrNull,
          email: emailOrNull,
          groupId: groupIdOrNull,
          openingBalance,
        })
        .subscribe({
          next: (result) => {
            this.saving.set(false);
            this.router.navigate(['/organizations', this.organizationId, 'contacts', result.id]);
          },
          error: (err: unknown) => {
            this.saving.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not create contact. Please try again.');
          },
        });
      return;
    }

    this.contactsService
      .updateContact(this.organizationId, this.routeContactId, {
        name,
        address: addressOrNull,
        pan: panOrNull,
        phone: phoneOrNull,
        email: emailOrNull,
        groupId: groupIdOrNull,
        openingBalance,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editing.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.saving.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update contact. Please try again.');
        },
      });
  }

  protected deactivate(): void {
    this.optionsOpen.set(false);
    this.contactsService.deactivateContact(this.organizationId, this.routeContactId).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not deactivate contact. Please try again.');
      },
    });
  }

  protected groupName(groupId: string | null): string {
    if (!groupId) {
      return '—';
    }
    return this.groups().find((g) => g.id === groupId)?.name ?? '—';
  }

  /** 'reports/customer-statement'/'reports/supplier-statement' -- no Statement report exists for a
   * Lead (Ageing/Statement are AR/AP-only per Phase 9), so the Overview card's "View Full Statement"
   * link is only rendered for Customer/Supplier. */
  protected statementRoute(): 'customer-statement' | 'supplier-statement' | null {
    switch (this.contact()?.type) {
      case 'Customer':
        return 'customer-statement';
      case 'Supplier':
        return 'supplier-statement';
      default:
        return null;
    }
  }

  private load(): void {
    this.loading.set(true);
    this.contactsService.getContact(this.organizationId, this.routeContactId).subscribe({
      next: (contact) => {
        this.contact.set(contact);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load contact.');
      },
    });
    this.loadOverview();
  }

  private loadOverview(): void {
    this.overviewLoading.set(true);
    this.overviewError.set(null);
    this.contactsService.getContactOverview(this.organizationId, this.routeContactId).subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.overviewLoading.set(false);
      },
      error: (err: unknown) => {
        this.overviewLoading.set(false);
        this.overviewError.set(extractErrorMessage(err) ?? 'Could not load the financial overview.');
      },
    });
  }
}
