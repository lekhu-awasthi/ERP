import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { AttachmentRowDto } from '../../../core/contacts/contacts.models';
import { TabParent } from '../../../core/contacts/tab-parent';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

const ALLOWED_EXTENSIONS = ['.pdf', '.png', '.jpg', '.jpeg', '.gif', '.doc', '.docx', '.xls', '.xlsx', '.csv', '.txt'];
const MAX_SIZE_BYTES = 10 * 1024 * 1024;

/** The Documents tab (roadmap Phase 18; parameterised by TabParent in Phase 27a, so the same
 * component serves a Contact and all 15 transactional detail pages) -- live-confirmed Tigg shape: a drag-and-drop zone
 * ("Drop your files or Click to upload new document") over a plain flat list (no folders/
 * thumbnails needed for MVP). Client-side extension/size checks mirror the server's real gate
 * (10MB, the same allowed-extension list) purely for immediate feedback -- the server is the
 * actual enforcement point. */
@Component({
  selector: 'app-attachment-list',
  imports: [PaginationControl, DatePipe],
  templateUrl: './attachment-list.html',
})
export class AttachmentList implements OnInit {
  private readonly contactsService = inject(ContactsService);

  readonly organizationId = input.required<string>();
  readonly parent = input.required<TabParent>();

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<AttachmentRowDto[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly dragging = signal(false);
  protected readonly uploading = signal(false);

  ngOnInit(): void {
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

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.upload(files[0]);
    }
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.upload(file);
    }
    input.value = '';
  }

  protected download(row: AttachmentRowDto): void {
    this.contactsService.downloadAttachment(this.organizationId(), row.id).subscribe({
      next: (blob) => triggerBlobDownload(blob, row.fileName),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not download the file. Please try again.');
      },
    });
  }

  protected remove(row: AttachmentRowDto): void {
    if (!window.confirm(`Delete "${row.fileName}"? This cannot be undone.`)) {
      return;
    }
    this.contactsService.deleteAttachment(this.organizationId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete the file. Please try again.');
      },
    });
  }

  protected formatSize(sizeBytes: number): string {
    if (sizeBytes < 1024) {
      return `${sizeBytes} B`;
    }
    if (sizeBytes < 1024 * 1024) {
      return `${(sizeBytes / 1024).toFixed(1)} KB`;
    }
    return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private upload(file: File): void {
    this.errorMessage.set(null);

    const extension = file.name.toLowerCase().slice(file.name.lastIndexOf('.'));
    if (!ALLOWED_EXTENSIONS.includes(extension)) {
      this.errorMessage.set(`Unsupported file type "${extension}". Allowed: ${ALLOWED_EXTENSIONS.join(', ')}.`);
      return;
    }
    if (file.size > MAX_SIZE_BYTES) {
      this.errorMessage.set('File is larger than the 10MB limit.');
      return;
    }

    this.uploading.set(true);
    this.contactsService.uploadAttachment(this.organizationId(), this.parent(), file).subscribe({
      next: () => {
        this.uploading.set(false);
        this.page.set(1);
        this.load();
      },
      error: (err: unknown) => {
        this.uploading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not upload the file. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.contactsService.listAttachments(this.organizationId(), this.parent(), this.page(), this.pageSize()).subscribe({
      next: (result) => {
        this.rows.set(result.rows);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load documents.');
      },
    });
  }
}
