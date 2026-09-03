import { Component, computed, input, signal } from '@angular/core';

import { TabParent } from '../../core/contacts/tab-parent';
import { DocumentType } from '../../core/sales/sales.models';
import { TaskParentType } from '../../core/workflow/workflow.models';
import { ActivityPanel } from '../../features/contacts/activity-panel/activity-panel';
import { AttachmentList } from '../../features/contacts/attachment-list/attachment-list';
import { TaskList } from '../../features/workflow/task-list/task-list';

export type DocumentTab = 'Overview' | 'Tasks' | 'Documents' | 'Activity';

/**
 * Phase 27a -- the Tasks / Documents / Activity tabs on a transactional document's detail page.
 *
 * <b>One shared component, not fifteen.</b> The live pass opened Invoice (Sales), Journal Voucher
 * (Accounting) and Warehouse Transfer (Inventory) and found byte-identical chrome on all three: a
 * vertical tab list reading Overview / Tasks / Documents / Activity over the same three panes.
 * Nothing about any pane is type-specific -- the Tasks pane is the same table with the same
 * "+ ADD TASK" action, the Documents pane the same bare dropzone, the Activity pane the same
 * composer over Comments / Activities / Emails. Fifteen per-type components would have been fifteen
 * copies of one thing, and silent drift between copies is precisely what a sweep phase is at risk of.
 *
 * <b>How a host page uses it</b> -- one element and one wrapper, no TypeScript change:
 * <pre>
 *   &lt;app-document-tabs #docTabs [organizationId]="organizationId" documentType="Invoice"
 *                        [documentId]="invoice()?.id ?? null" /&gt;
 *   &#64;if (docTabs.isOverview()) { ...the page's existing body... }
 * </pre>
 * Overview stays with the host because it genuinely is per-type: it is the document itself.
 *
 * A null <c>documentId</c> (an unsaved `.../new` form) renders no tab strip at all and reports
 * Overview, so the host needs no second condition -- there is nothing to attach a task or a file to
 * until the document exists.
 *
 * The three panes are the Phase 18 Contact components unchanged in behaviour, parameterised by
 * TabParent: reused rather than reimplemented, per phase-18 decision #2's refusal to treat the same
 * concept as two.
 */
@Component({
  selector: 'app-document-tabs',
  imports: [TaskList, AttachmentList, ActivityPanel],
  templateUrl: './document-tabs.html',
})
export class DocumentTabs {
  readonly organizationId = input.required<string>();
  readonly documentType = input.required<DocumentType>();
  readonly documentId = input<string | null>(null);

  private readonly selectedTab = signal<DocumentTab>('Overview');

  protected readonly tabs: readonly DocumentTab[] = ['Overview', 'Tasks', 'Documents', 'Activity'];

  /** Always Overview until the document exists -- there is nothing else to show for a draft form. */
  readonly activeTab = computed<DocumentTab>(() => (this.documentId() ? this.selectedTab() : 'Overview'));

  protected readonly parent = computed<TabParent | null>(() => {
    const documentId = this.documentId();
    return documentId ? { kind: 'Document', documentType: this.documentType(), documentId } : null;
  });

  /**
   * The Tasks endpoint has taken (parentType, parentId) since Phase 13, and Phase 27a widened
   * TaskParentType with members named exactly as DocumentType's. That name alignment is what makes
   * this a cast rather than a lookup table -- and it is not left to hope: the server-side
   * DocumentMechanismSweepGuardTests fails the build if the two enums ever diverge.
   */
  protected readonly taskParentType = computed<TaskParentType>(() => this.documentType() as TaskParentType);

  switchTab(tab: DocumentTab): void {
    this.selectedTab.set(tab);
  }

  /** Hosts wrap their own body in `@if (docTabs.isOverview())`. */
  isOverview(): boolean {
    return this.activeTab() === 'Overview';
  }
}
