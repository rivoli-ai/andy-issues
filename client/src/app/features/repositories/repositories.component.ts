// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  ApiService,
  Repository,
  PagedResult,
  SyncResult,
} from '../../shared/services/api.service';

@Component({
  selector: 'app-repositories',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <h1>Repositories</h1>

    <div class="toolbar">
      <div class="filters">
        <select [(ngModel)]="scope" (ngModelChange)="load()">
          <option value="mine">My repositories</option>
          <option value="shared">Shared with me</option>
          <option value="all">All</option>
        </select>
        <input class="input" placeholder="Search..." [(ngModel)]="search" (input)="load()" />
      </div>
      <div class="actions">
        <button class="btn-primary" (click)="showSyncGitHub = true">Sync GitHub</button>
        <button class="btn-secondary" (click)="showSyncAzdo = true">Sync Azure DevOps</button>
      </div>
    </div>

    <!-- Sync GitHub modal -->
    <div class="modal-backdrop" *ngIf="showSyncGitHub" (click)="showSyncGitHub = false">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>Sync GitHub Repositories</h2>
        <p class="hint">Enter comma-separated repo full names (owner/repo)</p>
        <textarea class="input textarea" [(ngModel)]="syncGitHubNames" rows="3" placeholder="owner/repo1, owner/repo2"></textarea>
        <div class="modal-actions">
          <button class="btn-secondary" (click)="showSyncGitHub = false">Cancel</button>
          <button class="btn-primary" (click)="doSyncGitHub()" [disabled]="!syncGitHubNames.trim()">Sync</button>
        </div>
        <p *ngIf="syncMessage" class="sync-msg">{{ syncMessage }}</p>
      </div>
    </div>

    <!-- Sync AzDO modal -->
    <div class="modal-backdrop" *ngIf="showSyncAzdo" (click)="showSyncAzdo = false">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>Sync Azure DevOps Repositories</h2>
        <input class="input" placeholder="Organization" [(ngModel)]="azdoOrg" />
        <input class="input" placeholder="Project (optional)" [(ngModel)]="azdoProject" />
        <textarea class="input textarea" [(ngModel)]="azdoRepoIds" rows="3" placeholder="Comma-separated repo IDs"></textarea>
        <div class="modal-actions">
          <button class="btn-secondary" (click)="showSyncAzdo = false">Cancel</button>
          <button class="btn-primary" (click)="doSyncAzdo()" [disabled]="!azdoOrg.trim() || !azdoRepoIds.trim()">Sync</button>
        </div>
        <p *ngIf="syncMessage" class="sync-msg">{{ syncMessage }}</p>
      </div>
    </div>

    <!-- Share modal -->
    <div class="modal-backdrop" *ngIf="shareRepo" (click)="shareRepo = null">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>Share "{{ shareRepo!.name }}"</h2>
        <input class="input" placeholder="User email" [(ngModel)]="shareEmail" />
        <div class="modal-actions">
          <button class="btn-secondary" (click)="shareRepo = null">Cancel</button>
          <button class="btn-primary" (click)="doShare()" [disabled]="!shareEmail.trim()">Share</button>
        </div>
        <p *ngIf="shareMessage" class="sync-msg">{{ shareMessage }}</p>
      </div>
    </div>

    <!-- Repository table -->
    <table class="data-table" *ngIf="repos.length > 0">
      <thead>
        <tr>
          <th>Name</th>
          <th>Provider</th>
          <th>Clone URL</th>
          <th>Azure ID</th>
          <th>Code Index</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <tr *ngFor="let repo of filteredRepos">
          <td>
            <a [routerLink]="['/backlog', repo.id]">{{ repo.name }}</a>
          </td>
          <td><span class="badge badge-provider">{{ repo.provider }}</span></td>
          <td class="url-cell">{{ repo.cloneUrl }}</td>
          <td>{{ repo.hasAzureIdentity ? 'Yes' : '-' }}</td>
          <td>{{ repo.codeIndexStatus }}</td>
          <td class="actions-cell">
            <button class="btn-sm btn-secondary" (click)="shareRepo = repo">Share</button>
            <button class="btn-sm btn-secondary btn-danger" (click)="confirmDelete(repo)">Delete</button>
          </td>
        </tr>
      </tbody>
    </table>

    <p *ngIf="repos.length === 0 && !loading" class="empty">No repositories. Sync from GitHub or Azure DevOps.</p>

    <div class="pagination" *ngIf="totalPages > 1">
      <button class="btn-secondary btn-sm" [disabled]="page <= 1" (click)="page = page - 1; load()">Previous</button>
      <span>Page {{ page }} of {{ totalPages }}</span>
      <button class="btn-secondary btn-sm" [disabled]="page >= totalPages" (click)="page = page + 1; load()">Next</button>
    </div>
  `,
  styles: [`
    h1 { margin-bottom: 20px; }
    .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; gap: 12px; flex-wrap: wrap; }
    .filters { display: flex; gap: 8px; flex: 1; }
    .filters select, .filters .input { padding: 8px 12px; border: 1px solid var(--border); border-radius: 4px; font-size: 14px; }
    .filters .input { flex: 1; max-width: 300px; }
    .actions { display: flex; gap: 8px; }
    .data-table { width: 100%; border-collapse: collapse; background: var(--surface); border-radius: 8px; overflow: hidden; }
    th, td { padding: 10px 14px; text-align: left; border-bottom: 1px solid var(--border); font-size: 13px; }
    th { background: var(--background); font-weight: 600; color: var(--text-secondary); font-size: 12px; text-transform: uppercase; letter-spacing: 0.5px; }
    .url-cell { max-width: 280px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: monospace; font-size: 12px; }
    .actions-cell { white-space: nowrap; }
    .badge { padding: 2px 8px; border-radius: 12px; font-size: 11px; font-weight: 500; }
    .badge-provider { background: #e8f0fe; color: var(--primary); }
    .btn-sm { padding: 4px 10px; font-size: 12px; }
    .btn-danger { color: var(--error); border-color: var(--error); }
    .btn-danger:hover { background: #fce8e6; }
    .empty { color: var(--text-secondary); font-size: 14px; padding: 24px 0; }
    .pagination { display: flex; align-items: center; gap: 12px; margin-top: 16px; font-size: 13px; }

    .modal-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; z-index: 100; }
    .modal { background: var(--surface); border-radius: 12px; padding: 24px; min-width: 400px; max-width: 500px; }
    .modal h2 { font-size: 18px; margin-bottom: 16px; }
    .modal .input { width: 100%; padding: 8px 12px; border: 1px solid var(--border); border-radius: 4px; font-size: 14px; margin-bottom: 12px; }
    .modal .textarea { font-family: monospace; resize: vertical; }
    .modal-actions { display: flex; gap: 8px; justify-content: flex-end; }
    .sync-msg { margin-top: 12px; font-size: 13px; color: var(--text-secondary); }
    .hint { font-size: 13px; color: var(--text-secondary); margin-bottom: 12px; }
  `],
})
export class RepositoriesComponent implements OnInit {
  repos: Repository[] = [];
  scope = 'mine';
  search = '';
  page = 1;
  pageSize = 20;
  totalCount = 0;
  loading = false;

  showSyncGitHub = false;
  syncGitHubNames = '';
  showSyncAzdo = false;
  azdoOrg = '';
  azdoProject = '';
  azdoRepoIds = '';
  syncMessage = '';

  shareRepo: Repository | null = null;
  shareEmail = '';
  shareMessage = '';

  constructor(private api: ApiService) {}

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  get filteredRepos(): Repository[] {
    if (!this.search.trim()) return this.repos;
    const q = this.search.toLowerCase();
    return this.repos.filter(r =>
      r.name.toLowerCase().includes(q) || r.cloneUrl.toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.api.listRepositories(this.scope, this.page, this.pageSize).subscribe({
      next: (result) => {
        this.repos = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  doSyncGitHub(): void {
    const ids = this.syncGitHubNames.split(',').map(s => s.trim()).filter(Boolean);
    this.api.syncGitHub(ids).subscribe({
      next: (r) => {
        this.syncMessage = `Added ${r.added}, updated ${r.updated}, skipped ${r.skipped}`;
        this.load();
      },
      error: (e) => { this.syncMessage = e.error?.error || 'Sync failed'; },
    });
  }

  doSyncAzdo(): void {
    const ids = this.azdoRepoIds.split(',').map(s => s.trim()).filter(Boolean);
    this.api.syncAzureDevOps(this.azdoOrg, ids, this.azdoProject || undefined).subscribe({
      next: (r) => {
        this.syncMessage = `Added ${r.added}, updated ${r.updated}, skipped ${r.skipped}`;
        this.load();
      },
      error: (e) => { this.syncMessage = e.error?.error || 'Sync failed'; },
    });
  }

  doShare(): void {
    if (!this.shareRepo) return;
    this.api.shareRepository(this.shareRepo.id, this.shareEmail).subscribe({
      next: () => {
        this.shareMessage = `Shared successfully`;
        this.shareEmail = '';
      },
      error: (e) => { this.shareMessage = e.error?.error || 'Share failed'; },
    });
  }

  confirmDelete(repo: Repository): void {
    if (confirm(`Delete repository "${repo.name}"?`)) {
      this.api.deleteRepository(repo.id).subscribe(() => this.load());
    }
  }
}
