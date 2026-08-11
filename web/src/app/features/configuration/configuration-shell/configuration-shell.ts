import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

/**
 * Configurations nav hub (roadmap Phase 2 task 5) -- mirrors organization-dashboard-page's card
 * chrome. Links to the lookup-list screens built this phase (CreditTerm, PaymentMode); the
 * remaining lookups (CustomStatus, ReportingTag*, CustomFieldDefinition) have working APIs
 * (Application/Api layers) but no Angular screen yet -- see phase-2-status.md's scope decisions.
 */
@Component({
  selector: 'app-configuration-shell',
  imports: [RouterLink],
  templateUrl: './configuration-shell.html',
})
export class ConfigurationShell {
  private readonly route = inject(ActivatedRoute);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
}
