import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { RolePermissionMatrix } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * The Role Reference permission-matrix editor (Phase 14) -- every PermissionKeys.cs constant
 * (105+ as of this phase), grouped by module, as one big checkbox grid with a single Save.
 *
 * A system role (Admin/Member) renders read-only -- see UpdateRolePermissionsCommandHandler's own
 * doc comment for why their RolePermission rows are shared globally across every Organization and
 * so deliberately not editable through this per-tenant UI.
 */
@Component({
  selector: 'app-role-permission-matrix-page',
  imports: [RouterLink],
  templateUrl: './role-permission-matrix-page.html',
})
export class RolePermissionMatrixPage {
  private readonly route = inject(ActivatedRoute);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly roleId = this.route.snapshot.paramMap.get('roleId')!;

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly matrix = signal<RolePermissionMatrix | null>(null);
  protected readonly grants = signal<Record<string, boolean>>({});

  constructor() {
    this.load();
  }

  protected toggle(permissionKey: string, checked: boolean): void {
    this.saved.set(false);
    this.grants.update((g) => ({ ...g, [permissionKey]: checked }));
  }

  protected toggleModule(module: string, checked: boolean): void {
    const group = this.matrix()?.groups.find((g) => g.module === module);
    if (!group) {
      return;
    }
    this.saved.set(false);
    this.grants.update((g) => {
      const next = { ...g };
      for (const entry of group.permissions) {
        next[entry.permissionKey] = checked;
      }
      return next;
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);

    this.organizationsService.updateRolePermissions(this.organizationId, this.roleId, { grants: this.grants() }).subscribe({
      next: () => {
        this.saving.set(false);
        this.saved.set(true);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not save permissions. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.organizationsService.getRolePermissionMatrix(this.organizationId, this.roleId).subscribe({
      next: (matrix) => {
        this.matrix.set(matrix);
        const grants: Record<string, boolean> = {};
        for (const group of matrix.groups) {
          for (const entry of group.permissions) {
            grants[entry.permissionKey] = entry.isGranted;
          }
        }
        this.grants.set(grants);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the permission matrix.');
      },
    });
  }
}
