import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';

/**
 * Placeholder landing page proving the auth guard actually blocks unauthenticated access
 * (roadmap Phase 1a task 4). The real Organization dashboard shell arrives in Phase 1b.
 */
@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly currentUser = this.authService.currentUser;

  protected logout(): void {
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
