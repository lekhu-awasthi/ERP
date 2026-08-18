import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { OrganizationMember, Role } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * The Role Reference list page (Phase 14) -- the two shared system roles (Admin/Member, read-only
 * here, see Role's own doc comment for why) plus this Organization's own custom roles, with
 * Create/Edit/Delete for the latter. Same inline-create/edit-form-plus-list chrome as
 * credit-term-list-page (this codebase's established Configuration-lookup CRUD pattern).
 */
@Component({
  selector: 'app-role-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './role-list-page.html',
})
export class RoleListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);
  private readonly fb = inject(FormBuilder);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly roles = signal<Role[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingDeleteId = signal<string | null>(null);

  protected readonly members = signal<OrganizationMember[]>([]);
  protected readonly reassigningMembershipId = signal<string | null>(null);
  protected readonly membersErrorMessage = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(50)]],
    description: [''],
  });

  constructor() {
    this.load();
    this.loadMembers();
  }

  protected reassignRole(member: OrganizationMember, roleId: string): void {
    if (!roleId || roleId === member.roleId) {
      return;
    }

    this.reassigningMembershipId.set(member.membershipId);
    this.membersErrorMessage.set(null);

    this.organizationsService.updateMembershipRole(this.organizationId, member.membershipId, { roleId }).subscribe({
      next: () => {
        this.reassigningMembershipId.set(null);
        this.loadMembers();
      },
      error: (err: unknown) => {
        this.reassigningMembershipId.set(null);
        this.membersErrorMessage.set(extractErrorMessage(err) ?? 'Could not reassign role. Please try again.');
      },
    });
  }

  private loadMembers(): void {
    this.organizationsService.listMembers(this.organizationId).subscribe({
      next: (members) => this.members.set(members),
      error: (err: unknown) => {
        this.membersErrorMessage.set(extractErrorMessage(err) ?? 'Could not load members.');
      },
    });
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset({ name: '', description: '' });
  }

  protected startEdit(role: Role): void {
    this.editingId.set(role.id);
    this.form.reset({ name: role.name, description: role.description ?? '' });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);

    const { name, description } = this.form.getRawValue();
    const request = { name, description: description || null };
    const editingId = this.editingId();

    const request$ = editingId
      ? this.organizationsService.updateRole(this.organizationId, editingId, request)
      : this.organizationsService.createRole(this.organizationId, request);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.startCreate();
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save role. Please try again.');
      },
    });
  }

  protected requestDelete(role: Role): void {
    this.confirmingDeleteId.set(role.id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected confirmDelete(role: Role): void {
    this.organizationsService.deleteRole(this.organizationId, role.id).subscribe({
      next: () => {
        this.confirmingDeleteId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.confirmingDeleteId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not delete role. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.listRoles(this.organizationId).subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load roles.');
      },
    });
  }
}
