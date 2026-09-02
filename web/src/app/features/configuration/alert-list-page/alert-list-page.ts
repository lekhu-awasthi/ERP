import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import {
  AlertDefinition,
  AlertSendLog,
  AlertType,
} from '../../../core/configuration/configuration.models';

/**
 * Roadmap Phase 20e / FR-11.1 -- Configurations > Apps > Alert Scheduler.
 *
 * Mirrors the reference product's own screen (confirmed live): a grid of alerts with Alert Name /
 * Medium / Type / Recipient / Schedule columns, a "Show Inactive" toggle, per-row Edit / Delete /
 * Mark As Inactive, and an "Email Logs" view reached from the panel's own menu (rendered here as a
 * togglable section rather than a slide-over, matching this app's existing card layout).
 *
 * There is deliberately no "Run now" button: the reference product has none, and adding one would
 * be an authenticated way to make the server send mail on demand. See the status doc.
 */
@Component({
  selector: 'app-alert-list-page',
  imports: [ReactiveFormsModule, RouterLink, NepaliDatePipe],
  templateUrl: './alert-list-page.html',
})
export class AlertListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly configurationService = inject(ConfigurationService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<AlertDefinition[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);
  protected readonly showInactive = signal(false);

  protected readonly logsOpen = signal(false);
  protected readonly logsLoading = signal(false);
  protected readonly logs = signal<AlertSendLog[]>([]);

  protected readonly alertTypes: readonly { value: AlertType; label: string }[] = [
    { value: 'DailyTransactionSummary', label: 'Daily Transaction Summary' },
    { value: 'CrmReport', label: 'CRM Report' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    alertType: ['DailyTransactionSummary' as AlertType, [Validators.required]],
    recipients: ['', [Validators.required, Validators.maxLength(1000)]],
    scheduleTime: ['09:00', [Validators.required]],
    isActive: [true],
  });

  constructor() {
    this.load();
  }

  /** Filtered in a plain method rather than a computed(): this reads two signals and is called from
   * the template, so it stays correct in a zoneless app without the FormControl-inside-computed()
   * trap phase 17 hit. */
  protected visibleItems(): AlertDefinition[] {
    const all = this.items();
    return this.showInactive() ? all : all.filter((item) => item.isActive);
  }

  protected alertTypeLabel(type: AlertType): string {
    return this.alertTypes.find((option) => option.value === type)?.label ?? type;
  }

  /** The API serializes TimeOnly as "HH:mm:ss"; the <input type="time"> control wants "HH:mm". */
  protected displayTime(scheduleTime: string): string {
    return scheduleTime.slice(0, 5);
  }

  protected toggleShowInactive(): void {
    this.showInactive.update((value) => !value);
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({
      name: '',
      alertType: 'DailyTransactionSummary',
      recipients: '',
      scheduleTime: '09:00',
      isActive: true,
    });
  }

  protected startEdit(item: AlertDefinition): void {
    this.editingId.set(item.id);
    this.form.reset({
      name: item.name,
      alertType: item.alertType,
      recipients: item.recipients,
      scheduleTime: this.displayTime(item.scheduleTime),
      isActive: item.isActive,
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, alertType, recipients, scheduleTime, isActive } = this.form.getRawValue();
    const editingId = this.editingId();

    // Medium and Frequency are sent as constants, not form fields: the reference product renders
    // both as dropdowns with exactly one option each (confirmed live), so a picker here would be a
    // control the user can never change.
    const base = {
      name,
      medium: 'Email' as const,
      alertType,
      recipients,
      frequency: 'Daily' as const,
      scheduleTime: `${scheduleTime}:00`,
    };

    const request$ = editingId
      ? this.configurationService.updateAlert(this.organizationId, editingId, { ...base, isActive })
      : this.configurationService.createAlert(this.organizationId, base);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save alert. Please try again.');
      },
    });
  }

  protected toggleActive(item: AlertDefinition): void {
    this.configurationService.setAlertActive(this.organizationId, item.id, !item.isActive).subscribe({
      next: () => this.load(),
      error: (err: unknown) =>
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update the alert. Please try again.'),
    });
  }

  protected requestDelete(item: AlertDefinition): void {
    this.confirmingDeleteId.set(item.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(item: AlertDefinition): void {
    this.configurationService.deleteAlert(this.organizationId, item.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete alert. Please try again.');
      },
    });
  }

  protected toggleLogs(): void {
    const opening = !this.logsOpen();
    this.logsOpen.set(opening);
    if (opening) {
      this.loadLogs();
    }
  }

  private loadLogs(): void {
    this.logsLoading.set(true);
    this.configurationService.listAlertSendLogs(this.organizationId).subscribe({
      next: (result) => {
        this.logs.set(result.items);
        this.logsLoading.set(false);
      },
      error: (err: unknown) => {
        this.logsLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load email logs.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.configurationService.listAlerts(this.organizationId).subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load alerts.');
      },
    });
  }
}
