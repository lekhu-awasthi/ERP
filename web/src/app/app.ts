import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { environment } from '../environments/environment';

/**
 * Phase 0 shell: proves the Angular workspace can build, serve, and reach the Api's
 * /health endpoint end to end (roadmap Phase 0 task 5). Real routing/feature modules
 * start arriving in Phase 1 (Identity & Tenancy: Register/Login/Org wizard).
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly title = signal('ErpApp');
  protected readonly apiStatus = signal<'checking' | 'healthy' | 'unreachable'>('checking');

  private readonly http = inject(HttpClient);

  ngOnInit(): void {
    this.http.get<{ status: string }>(`${environment.apiBaseUrl}/health`).subscribe({
      next: () => this.apiStatus.set('healthy'),
      error: () => this.apiStatus.set('unreachable'),
    });
  }
}
