import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { TaskList } from '../task-list/task-list';

/**
 * Phase 39 — `Workflow > Tasks` as a screen of its own, the other half of phase 34b's carried
 * item #6.
 *
 * <b>It lists the whole organization, and that is a live finding rather than a design choice.</b>
 * Phase 13 wrote `ListTasksQuery` parent-scoped on purpose and called an org-wide feed "speculative
 * … which erp-module-scan.md never confirms exists" — correct on the evidence it had. The live pass
 * (2026-09-13) opened the screen: it lists tasks across every parent, with columns Due / Created At
 * / Title / Type / Priority / Created By / Assigned To and <b>no parent column at all</b>. So the
 * query gained a nullable parent rather than a parent picker, and {@link TaskList} serves a third
 * host with no template change.
 *
 * A task created from here is parented to the Organization — the only parent available when the user
 * did not arrive from a record, and the same one the dashboard's task section already uses.
 */
@Component({
  selector: 'app-task-list-page',
  imports: [TaskList],
  templateUrl: './task-list-page.html',
})
export class TaskListPage {
  private readonly route = inject(ActivatedRoute);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
}
