// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService, Repository, Sandbox } from '../../shared/services/api.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <h1>Dashboard</h1>
    <div class="stats">
      <div class="stat-card">
        <div class="stat-value">{{ repoCount }}</div>
        <div class="stat-label">Repositories</div>
      </div>
      <div class="stat-card">
        <div class="stat-value">{{ sandboxCount }}</div>
        <div class="stat-label">Sandboxes</div>
      </div>
      <div class="stat-card">
        <div class="stat-value">{{ runningSandboxes }}</div>
        <div class="stat-label">Running</div>
      </div>
    </div>
    <div class="quick-links">
      <a routerLink="/repositories">Manage Repositories &rarr;</a>
      <a routerLink="/sandboxes">View Sandboxes &rarr;</a>
      <a routerLink="/settings">Settings &rarr;</a>
    </div>
  `,
  styles: [`
    h1 { margin-bottom: 24px; }
    .stats { display: flex; gap: 16px; margin-bottom: 24px; }
    .stat-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 24px;
      min-width: 160px;
      text-align: center;
    }
    .stat-value { font-size: 32px; font-weight: 600; color: var(--primary); }
    .stat-label { font-size: 14px; color: var(--text-secondary); margin-top: 4px; }
    .quick-links { display: flex; gap: 24px; font-size: 14px; }
  `],
})
export class DashboardComponent implements OnInit {
  repoCount = 0;
  sandboxCount = 0;
  runningSandboxes = 0;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.listRepositories('mine', 1, 1).subscribe({
      next: (r) => { this.repoCount = r.totalCount; },
    });
    this.api.listSandboxes().subscribe({
      next: (list) => {
        this.sandboxCount = list.length;
        this.runningSandboxes = list.filter(s => s.status === 'Running').length;
      },
    });
  }
}
