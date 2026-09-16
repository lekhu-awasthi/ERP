import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { TaskRow } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { TaskForm } from '../task-form/task-form';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 48 (43 carried item #4) — the route that makes `UpdateTaskCommand` reachable.
 *
 * <p>A route rather than a modal, which is where the reference product puts it: this codebase has
 * no modal mechanism (phase 22 found Bootstrap's JS is not loaded at all), and a route is
 * linkable, back-button-correct and needs nothing new. The form itself is the list's, per phase 3's
 * one-component rule — see {@link TaskForm}.</p>
 *
 * <p>Reads its record through `listTasks`' `id` filter for the reason the detail page records: the
 * row is assembled from a type name, an assignee name and a creator name, and a second query would
 * be a second copy of that assembly.</p>
 */
@Component({
  selector: 'app-task-edit-page',
  imports: [RouterLink, TaskForm, StatusBanner],
  templateUrl: './task-edit-page.html',
})
export class TaskEditPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly workflowService = inject(WorkflowService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly taskId = this.route.snapshot.paramMap.get('taskId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly task = signal<TaskRow | null>(null);

  constructor() {
    this.load();
  }

  protected back(): void {
    void this.router.navigate(['/organizations', this.organizationId, 'workflow', 'tasks', this.taskId]);
  }

  private load(): void {
    this.loading.set(true);
    this.workflowService.listTasks(this.organizationId, null, null, null, 1, 1, null, this.taskId).subscribe({
      next: (result) => {
        this.loading.set(false);
        const row = result.rows[0] ?? null;
        this.task.set(row);
        if (!row) {
          this.errorMessage.set('Task not found.');
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
