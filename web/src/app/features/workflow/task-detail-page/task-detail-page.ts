import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { TabParent } from '../../../core/contacts/tab-parent';
import { TaskRow } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { ActivityPanel } from '../../contacts/activity-panel/activity-panel';
import { AttachmentList } from '../../contacts/attachment-list/attachment-list';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

type TaskTab = 'Overview' | 'Documents' | 'Activity';

/**
 * Phase 43 (39 carried item #1) — the Task detail page.
 *
 * <b>Three tabs, not five, and the difference is the finding.</b> The live page (2026-09-13) reads
 * Overview / Documents / Activity with a `MARK AS DONE` action — no Tasks tab, because a task does
 * not parent tasks. That is why `WorkTask` joined `AttachmentParentType` and `CommentParentType` and
 * deliberately not `TaskParentType`, and why `DocumentMechanismSweepGuardTests` asserts the
 * asymmetry in both directions rather than leaving it looking like an omission.
 *
 * Reads its record through `listTasks`' new `id` filter, for the reason the Deal page records: the
 * row is assembled from a type name, an assignee name and a creator name, and a second query would
 * be a second copy of that assembly.
 */
@Component({
  selector: 'app-task-detail-page',
  imports: [RouterLink, AttachmentList, ActivityPanel, NepaliDatePipe, StatusBanner],
  templateUrl: './task-detail-page.html',
})
export class TaskDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly workflowService = inject(WorkflowService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly taskId = this.route.snapshot.paramMap.get('taskId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly task = signal<TaskRow | null>(null);

  protected readonly tabs: readonly TaskTab[] = ['Overview', 'Documents', 'Activity'];
  protected readonly activeTab = signal<TaskTab>('Overview');

  protected readonly parent = computed<TabParent>(() => ({ kind: 'Task', taskId: this.taskId }));

  constructor() {
    this.load();
  }

  protected switchTab(tab: TaskTab): void {
    this.activeTab.set(tab);
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
