import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

/** One-time celebratory state after Organization creation (erp-module-scan.md's "Post-creation" section). */
@Component({
  selector: 'app-welcome-page',
  imports: [RouterLink],
  templateUrl: './welcome-page.html',
})
export class WelcomePage {
  private readonly route = inject(ActivatedRoute);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly organizationName = this.route.snapshot.queryParamMap.get('name') ?? 'your organization';
}
