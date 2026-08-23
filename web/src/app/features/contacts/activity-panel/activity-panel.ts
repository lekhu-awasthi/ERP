import { Component, OnInit, inject, input, signal } from '@angular/core';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ActivityRowDto, CommentRowDto } from '../../../core/contacts/contacts.models';
import { SmsLogRowDto } from '../../../core/crm/crm.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type ActivitySubTab = 'Comments' | 'Activities' | 'SmsHistory' | 'EmailLogs';

/** Contact Activity tab (roadmap Phase 18) -- live-confirmed Tigg shape: 4 sub-tabs (Comments /
 * Activities / SMS History / Email Logs). Email Logs has no backend capability at all (no entity,
 * no endpoint) so it renders an empty-state message only -- not a faked working tab. */
@Component({
  selector: 'app-activity-panel',
  imports: [PaginationControl],
  templateUrl: './activity-panel.html',
})
export class ActivityPanel implements OnInit {
  private readonly contactsService = inject(ContactsService);

  readonly organizationId = input.required<string>();
  readonly contactId = input.required<string>();

  protected readonly subTab = signal<ActivitySubTab>('Comments');
  protected readonly errorMessage = signal<string | null>(null);

  // Comments
  protected readonly commentRows = signal<CommentRowDto[]>([]);
  protected readonly commentsLoading = signal(true);
  protected readonly commentPage = signal(1);
  protected readonly commentPageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly commentTotalCount = signal(0);
  protected readonly newComment = signal('');
  protected readonly postingComment = signal(false);

  // Activities
  protected readonly activityRows = signal<ActivityRowDto[]>([]);
  protected readonly activitiesLoading = signal(true);
  protected readonly activityPage = signal(1);
  protected readonly activityPageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly activityTotalCount = signal(0);

  // SMS History
  protected readonly smsRows = signal<SmsLogRowDto[]>([]);
  protected readonly smsLoading = signal(true);
  protected readonly smsPage = signal(1);
  protected readonly smsPageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly smsTotalCount = signal(0);

  ngOnInit(): void {
    this.loadComments();
  }

  protected switchSubTab(tab: ActivitySubTab): void {
    this.subTab.set(tab);
    this.errorMessage.set(null);
    if (tab === 'Activities' && this.activityRows().length === 0) {
      this.loadActivities();
    } else if (tab === 'SmsHistory' && this.smsRows().length === 0) {
      this.loadSmsHistory();
    }
  }

  protected onCommentInput(event: Event): void {
    this.newComment.set((event.target as HTMLTextAreaElement).value);
  }

  protected postComment(): void {
    const content = this.newComment().trim();
    if (!content) {
      return;
    }
    this.postingComment.set(true);
    this.errorMessage.set(null);
    this.contactsService.addComment(this.organizationId(), this.contactId(), content).subscribe({
      next: () => {
        this.postingComment.set(false);
        this.newComment.set('');
        this.commentPage.set(1);
        this.loadComments();
      },
      error: (err: unknown) => {
        this.postingComment.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not post comment. Please try again.');
      },
    });
  }

  protected onCommentPageChange(page: number): void {
    this.commentPage.set(page);
    this.loadComments();
  }

  protected onCommentPageSizeChange(pageSize: number): void {
    this.commentPageSize.set(pageSize);
    this.commentPage.set(1);
    this.loadComments();
  }

  protected onActivityPageChange(page: number): void {
    this.activityPage.set(page);
    this.loadActivities();
  }

  protected onActivityPageSizeChange(pageSize: number): void {
    this.activityPageSize.set(pageSize);
    this.activityPage.set(1);
    this.loadActivities();
  }

  protected onSmsPageChange(page: number): void {
    this.smsPage.set(page);
    this.loadSmsHistory();
  }

  protected onSmsPageSizeChange(pageSize: number): void {
    this.smsPageSize.set(pageSize);
    this.smsPage.set(1);
    this.loadSmsHistory();
  }

  private loadComments(): void {
    this.commentsLoading.set(true);
    this.contactsService
      .listComments(this.organizationId(), this.contactId(), this.commentPage(), this.commentPageSize())
      .subscribe({
        next: (result) => {
          this.commentRows.set(result.rows);
          this.commentTotalCount.set(result.totalCount);
          this.commentsLoading.set(false);
        },
        error: (err: unknown) => {
          this.commentsLoading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load comments.');
        },
      });
  }

  private loadActivities(): void {
    this.activitiesLoading.set(true);
    this.contactsService
      .listActivities(this.organizationId(), this.contactId(), this.activityPage(), this.activityPageSize())
      .subscribe({
        next: (result) => {
          this.activityRows.set(result.rows);
          this.activityTotalCount.set(result.totalCount);
          this.activitiesLoading.set(false);
        },
        error: (err: unknown) => {
          this.activitiesLoading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load activities.');
        },
      });
  }

  private loadSmsHistory(): void {
    this.smsLoading.set(true);
    this.contactsService
      .listContactSmsHistory(this.organizationId(), this.contactId(), this.smsPage(), this.smsPageSize())
      .subscribe({
        next: (result) => {
          this.smsRows.set(result.rows);
          this.smsTotalCount.set(result.totalCount);
          this.smsLoading.set(false);
        },
        error: (err: unknown) => {
          this.smsLoading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load SMS history.');
        },
      });
  }
}
