import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { LocationGrantsInput, RolePermissionMatrix } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';

/**
 * The Role Reference permission-matrix editor (Phase 14) -- every PermissionKeys.cs constant,
 * grouped by module, as one big checkbox grid with a single Save.
 *
 * Phase 32b adds the second section the live editor has: **Organization-wide Permissions** ("Apply
 * across all billing locations") and **Location-specific Permissions** ("Scoped to individual
 * billing locations"), the latter one collapsible per location holding the transaction keys alone.
 * The two are independent stores -- confirmed live 2026-09-09, where a role showed 25 of 94
 * organization-wide transaction grants and 0 of 94 at every location -- so ticking a key in one
 * section never ticks it in the other, and an organization-wide grant already applies everywhere.
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

  /**
   * Location grants are keyed `${locationId}|${permissionKey}` in one flat map rather than a nested
   * structure: the template only ever asks about a single cell, and a flat key keeps the change
   * detection a plain object replace, the same shape the organization-wide half already uses.
   */
  protected readonly locationGrants = signal<Record<string, boolean>>({});

  /** Which location collapsibles are open. The live editor starts them all closed. */
  protected readonly openLocations = signal<Record<string, boolean>>({});

  protected readonly organizationWideEnabled = computed(() => {
    const g = this.grants();
    return Object.keys(g).filter((k) => g[k]).length;
  });

  protected readonly organizationWideTotal = computed(() => Object.keys(this.grants()).length);

  constructor() {
    this.load();
  }

  protected cellKey(locationId: string, permissionKey: string): string {
    return `${locationId}|${permissionKey}`;
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

  protected toggleLocation(locationId: string, permissionKey: string, checked: boolean): void {
    this.saved.set(false);
    this.locationGrants.update((g) => ({ ...g, [this.cellKey(locationId, permissionKey)]: checked }));
  }

  /** The live editor's per-location "Grant all", which covers that location's whole 94. */
  protected toggleLocationAll(locationId: string, checked: boolean): void {
    const section = this.matrix()?.locationSections.find((s) => s.locationId === locationId);
    if (!section) {
      return;
    }
    this.saved.set(false);
    this.locationGrants.update((g) => {
      const next = { ...g };
      for (const group of section.groups) {
        for (const entry of group.permissions) {
          next[this.cellKey(locationId, entry.permissionKey)] = checked;
        }
      }
      return next;
    });
  }

  protected locationEnabledCount(locationId: string): number {
    const section = this.matrix()?.locationSections.find((s) => s.locationId === locationId);
    if (!section) {
      return 0;
    }
    const g = this.locationGrants();
    return section.groups
      .flatMap((group) => group.permissions)
      .filter((entry) => g[this.cellKey(locationId, entry.permissionKey)]).length;
  }

  protected locationTotalCount(locationId: string): number {
    const section = this.matrix()?.locationSections.find((s) => s.locationId === locationId);
    return section ? section.groups.reduce((sum, group) => sum + group.permissions.length, 0) : 0;
  }

  protected isLocationOpen(locationId: string): boolean {
    return this.openLocations()[locationId] === true;
  }

  protected toggleLocationOpen(locationId: string): void {
    this.openLocations.update((o) => ({ ...o, [locationId]: !o[locationId] }));
  }

  protected save(): void {
    this.saving.set(true);
    this.saved.set(false);
    this.errorMessage.set(null);

    const locationGrants: LocationGrantsInput[] = (this.matrix()?.locationSections ?? []).map((section) => {
      const grants: Record<string, boolean> = {};
      for (const group of section.groups) {
        for (const entry of group.permissions) {
          grants[entry.permissionKey] = this.locationGrants()[this.cellKey(section.locationId, entry.permissionKey)] === true;
        }
      }
      return { locationId: section.locationId, grants };
    });

    this.organizationsService
      .updateRolePermissions(this.organizationId, this.roleId, { grants: this.grants(), locationGrants })
      .subscribe({
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

        const locationGrants: Record<string, boolean> = {};
        for (const section of matrix.locationSections ?? []) {
          for (const group of section.groups) {
            for (const entry of group.permissions) {
              locationGrants[this.cellKey(section.locationId, entry.permissionKey)] = entry.isGranted;
            }
          }
        }
        this.locationGrants.set(locationGrants);

        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the permission matrix.');
      },
    });
  }
}
