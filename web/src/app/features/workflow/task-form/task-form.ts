import { Component, OnInit, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { TaskType } from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { OrganizationMember } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { TaskParentType, TaskRow } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 48 (43 carried item #4) — <b>one Task form, serving create and edit.</b>
 *
 * <h4>What was actually wrong</h4>
 *
 * <p>Phase 43 recorded that "editing a Task still happens through the inline form on its list". It
 * does not: that form is <b>create-only</b>, and `updateTask` — a service method that has existed
 * since phase 13 — was called from nowhere in the app. `UpdateTaskCommand` was reachable over HTTP
 * and from no screen, which is phase-31's rule (*a field is reachable only if you can name the
 * command that writes it <b>and</b> the screen that calls it*) failing on the screen half. A typo in
 * a task's title was permanent.</p>
 *
 * <h4>Why one component and not an edit form beside the create form</h4>
 *
 * <p>Phase 3's rule — one component serving `.../new` and `.../:id` — applied to the create/edit
 * pair rather than to a route pair. It is also what the reference product does: `OPTION > Edit` on
 * the live Task detail page opens the <i>same</i> modal the create flow uses, prefilled, still
 * captioned "New Task" (read 2026-09-16). Two forms would be two validation surfaces over one
 * aggregate, and the one that is used less is the one that drifts.</p>
 *
 * <p>The field set is the live modal's, which is exactly `UpdateTaskCommand`'s: Title, Description,
 * Assign To, Due Date, Type, Priority, private. Nothing is shown that the command will not write.</p>
 */
@Component({
  selector: 'app-task-form',
  imports: [ReactiveFormsModule, BsDateInput, StatusBanner],
  templateUrl: './task-form.html',
})
export class TaskForm implements OnInit {
  private readonly workflowService = inject(WorkflowService);
  private readonly configurationService = inject(ConfigurationService);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  readonly organizationId = input.required<string>();

  /** The task being edited, or null to create a new one. */
  readonly task = input<TaskRow | null>(null);

  /** Where a newly created task hangs. Ignored when editing — a task does not change parent. */
  readonly parentType = input<TaskParentType | null>(null);
  readonly parentId = input<string | null>(null);

  /**
   * Prefix for every control's DOM id. Two hosts can be on one page (a list and, one day, a drawer),
   * and a duplicated id breaks the `<label for>` association that phase 34a's sweep guarantees —
   * `id="isPrivate"` was in fact already duplicated between the Deal and Task list forms.
   */
  readonly idPrefix = input<string>('task-form');

  readonly saved = output<void>();
  readonly cancelled = output<void>();

  protected readonly taskTypes = signal<TaskType[]>([]);
  protected readonly members = signal<OrganizationMember[]>([]);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly isEdit = computed(() => this.task() !== null);

  protected readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    assignedToUserId: [''],
    dueDate: [''],
    taskTypeId: ['', Validators.required],
    priority: ['Normal' as 'Normal' | 'Urgent', Validators.required],
    isPrivate: [false],
  });

  constructor() {
    // Prefill whenever the record arrives or changes. `untracked` around the write so this effect
    // depends on `task()` alone — an effect that reads what it writes is the NG0600 shape phase 35a
    // hit, and the form value is not a signal this should be tracking.
    effect(() => {
      const row = this.task();
      untracked(() => this.reset(row));
    });
  }

  ngOnInit(): void {
    this.configurationService.listTaskTypes(this.organizationId()).subscribe({
      next: (types) => this.taskTypes.set(types),
    });
    this.organizationsService.listMembers(this.organizationId()).subscribe({
      next: (members) => this.members.set(members),
    });
  }

  /** Re-seeds the form from the record (or to empty). Public so a host can re-open it clean. */
  reset(row: TaskRow | null = this.task()): void {
    this.errorMessage.set(null);
    this.form.reset({
      title: row?.title ?? '',
      description: row?.description ?? '',
      assignedToUserId: row?.assignedToUserId ?? '',
      dueDate: row?.dueDate ?? '',
      taskTypeId: row?.taskTypeId ?? '',
      priority: row?.priority ?? 'Normal',
      isPrivate: row?.isPrivate ?? false,
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

    const { title, description, assignedToUserId, dueDate, taskTypeId, priority, isPrivate } =
      this.form.getRawValue();

    const shared = {
      title,
      description: description || null,
      assignedToUserId: assignedToUserId || null,
      dueDate: dueDate || null,
      taskTypeId,
      priority,
      isPrivate,
    };

    const existing = this.task();

    // An explicit if/else rather than one shared `request$`, because the two results are different
    // types and a shared variable resolves to their union (phase-4 bug #3).
    if (existing) {
      this.workflowService.updateTask(this.organizationId(), existing.id, shared).subscribe({
        next: () => {
          this.saving.set(false);
          this.saved.emit();
        },
        error: (err: unknown) => this.fail(err, 'Could not save the task. Please try again.'),
      });
    } else {
      this.workflowService
        .createTask(this.organizationId(), {
          ...shared,
          parentType: this.parentType() ?? 'Organization',
          parentId: this.parentId() ?? this.organizationId(),
        })
        .subscribe({
          next: () => {
            this.saving.set(false);
            this.saved.emit();
          },
          error: (err: unknown) => this.fail(err, 'Could not create task. Please try again.'),
        });
    }
  }

  private fail(err: unknown, fallback: string): void {
    this.saving.set(false);
    this.errorMessage.set(extractErrorMessage(err) ?? fallback);
  }
}
