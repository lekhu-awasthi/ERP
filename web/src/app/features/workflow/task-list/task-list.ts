import { LowerCasePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { TaskParentType, TaskRow, TaskStatus } from '../../../core/workflow/workflow.models';
import { WorkflowService } from '../../../core/workflow/workflow.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { TaskForm } from '../task-form/task-form';

/**
 * Shared Task list component (roadmap Phase 13) -- reused, not duplicated, across its two
 * confirmed live integration points (Contact detail page's Tasks tab, Organization dashboard's
 * Tasks section), parameterized by (organizationId, parentType, parentId) the same "don't
 * duplicate, extract a shared reader" discipline Phase 10's ContactLedgerReader established for
 * query handlers, applied here to Angular. Three status sub-tabs (Pending/Started/Done, the
 * confirmed live UI shape) plus an inline create form and a per-row complete checkmark.
 */
@Component({
  selector: 'app-task-list',
  imports: [LowerCasePipe, RouterLink, PaginationControl, NepaliDatePipe, StatusBanner, TaskForm],
  templateUrl: './task-list.html',
})
export class TaskList implements OnInit {
  private readonly workflowService = inject(WorkflowService);

  readonly organizationId = input.required<string>();
  /**
   * Phase 39 -- both null on the standalone `Workflow > Tasks` screen, which lists every task in the
   * organization. The live list shows <b>no parent column</b>, so an unscoped list is genuinely the
   * same table rather than a different one, which is what let this component serve a third host with
   * no template change.
   *
   * <p>A task created from the unscoped list is parented to the Organization -- the same parent the
   * dashboard's own task section uses, and the only one available when the user did not start from a
   * record.</p>
   */
  readonly parentType = input<TaskParentType | null>(null);
  readonly parentId = input<string | null>(null);

  /** The search term, tracked in its own signal written by the input handler: the app is zoneless,
   * so a `computed()` over a FormControl's value caches forever (phase-17). */
  protected readonly search = signal('');

  protected readonly statuses: TaskStatus[] = ['Pending', 'Started', 'Done'];
  protected readonly activeStatus = signal<TaskStatus>('Pending');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<TaskRow[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly showCreateForm = signal(false);

  /**
   * Every filter on this list calls `page.set(1)` before reloading, and a swept-in one has to copy
   * what its siblings do rather than a template: without it, a narrowing search applied on page 3
   * reads as "there are no matching tasks" (phase-35b).
   */
  protected onSearchInput(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  // Reads required inputs, so this runs from ngOnInit (guaranteed after Angular has bound the
  // inputs), not the constructor (NG8118 -- an input() isn't readable that early).
  ngOnInit(): void {
    this.load();
  }

  protected switchTab(status: TaskStatus): void {
    this.activeStatus.set(status);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  protected toggleCreateForm(): void {
    this.showCreateForm.set(!this.showCreateForm());
    this.errorMessage.set(null);
  }

  /**
   * Phase 48 -- `app-task-form` owns the fields, the validation and the POST; this list owns only
   * the reload. A new task is always Pending, so switching to that tab is showing the user the row
   * they just made rather than a guess.
   */
  protected onCreated(): void {
    this.showCreateForm.set(false);
    this.activeStatus.set('Pending');
    this.load();
  }

  protected start(row: TaskRow): void {
    this.workflowService.updateTaskStatus(this.organizationId(), row.id, 'Started').subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update the task. Please try again.');
      },
    });
  }

  protected complete(row: TaskRow): void {
    this.workflowService.updateTaskStatus(this.organizationId(), row.id, 'Done').subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update the task. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.workflowService
      .listTasks(
        this.organizationId(), this.parentType(), this.parentId(), this.activeStatus(),
        this.page(), this.pageSize(), this.search().trim() || null)
      .subscribe({
        next: (result) => {
          this.rows.set(result.rows);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load tasks.');
        },
      });
  }
}
