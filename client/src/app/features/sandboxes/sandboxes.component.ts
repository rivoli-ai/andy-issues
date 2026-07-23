// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ApiService,
  Sandbox,
  SandboxConnection,
  Repository,
} from '../../shared/services/api.service';

@Component({
  selector: 'app-sandboxes',
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-header">
      <h1>Sandboxes</h1>
      <button class="btn-primary" (click)="showCreate = true">Create Sandbox</button>
    </div>

    <!-- Create modal -->
    <div class="modal-backdrop" *ngIf="showCreate" (click)="showCreate = false">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>Create Sandbox</h2>
        <select class="input" [(ngModel)]="createRepoId">
          <option value="">Select repository...</option>
          <option *ngFor="let r of repos" [value]="r.id">{{ r.name }}</option>
        </select>
        <input class="input" placeholder="Branch" [(ngModel)]="createBranch" />
        <div class="modal-actions">
          <button class="btn-secondary" (click)="showCreate = false">Cancel</button>
          <button class="btn-primary" (click)="doCreate()" [disabled]="!createRepoId || !createBranch.trim()">Create</button>
        </div>
      </div>
    </div>

    <!-- Sandbox cards -->
    <div class="sandbox-grid" *ngIf="sandboxes.length > 0">
      <div *ngFor="let s of sandboxes" class="sandbox-card">
        <div class="card-header">
          <span class="sandbox-status" [class]="'status-' + s.status.toLowerCase()">{{ s.status }}</span>
          <span class="sandbox-branch">{{ s.branch }}</span>
        </div>
        <div class="card-body">
          <p class="card-meta">Container: <code>{{ s.containerId | slice:0:12 }}</code></p>
          <p class="card-meta">Repo: {{ s.repositoryId | slice:0:8 }}...</p>
        </div>
        <div class="card-actions">
          <button class="btn-sm btn-secondary" (click)="connect(s)">Connect</button>
          <button class="btn-sm btn-secondary btn-danger" (click)="destroy(s)">Destroy</button>
        </div>

        <!-- Connection info -->
        <div *ngIf="connections[s.id]" class="connection-info">
          <p *ngIf="connections[s.id].ideEndpoint">
            IDE: <a [href]="connections[s.id].ideEndpoint!" target="_blank">{{ connections[s.id].ideEndpoint }}</a>
          </p>
          <p *ngIf="connections[s.id].vncEndpoint">
            VNC: <a [href]="connections[s.id].vncEndpoint!" target="_blank">{{ connections[s.id].vncEndpoint }}</a>
          </p>
          <p *ngIf="connections[s.id].sshEndpoint">
            SSH: <code>{{ connections[s.id].sshEndpoint }}</code>
          </p>
        </div>
      </div>
    </div>

    <p *ngIf="sandboxes.length === 0 && !loading" class="empty">No sandboxes. Create one to get started.</p>
  `,
  styles: [`
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
    .page-header h1 { margin: 0; }

    .sandbox-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; }
    .sandbox-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 16px; }
    .card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    .sandbox-status { padding: 2px 8px; border-radius: 12px; font-size: 11px; font-weight: 500; }
    .status-running { background: #e6f4ea; color: var(--success); }
    .status-creating { background: #fef7e0; color: #e37400; }
    .status-stopped { background: var(--background); color: var(--text-secondary); }
    .sandbox-branch { font-family: monospace; font-size: 13px; color: var(--primary); }
    .card-body { margin-bottom: 12px; }
    .card-meta { font-size: 12px; color: var(--text-secondary); margin: 2px 0; }
    .card-meta code { background: var(--background); padding: 1px 4px; border-radius: 3px; font-size: 11px; }
    .card-actions { display: flex; gap: 8px; }
    .btn-sm { padding: 4px 10px; font-size: 12px; }
    .btn-danger { color: var(--error); border-color: var(--error); }
    .connection-info { margin-top: 12px; padding-top: 12px; border-top: 1px solid var(--border); font-size: 12px; }
    .connection-info p { margin: 4px 0; }
    .connection-info a { color: var(--primary); }
    .connection-info code { background: var(--background); padding: 1px 4px; border-radius: 3px; font-size: 11px; }
    .empty { color: var(--text-secondary); font-size: 14px; padding: 24px 0; }

    .modal-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; z-index: 100; }
    .modal { background: var(--surface); border-radius: 12px; padding: 24px; min-width: 400px; }
    .modal h2 { font-size: 18px; margin-bottom: 16px; }
    .modal .input { width: 100%; padding: 8px 12px; border: 1px solid var(--border); border-radius: 4px; font-size: 14px; margin-bottom: 12px; }
    .modal-actions { display: flex; gap: 8px; justify-content: flex-end; }
  `],
})
export class SandboxesComponent implements OnInit, OnDestroy {
  sandboxes: Sandbox[] = [];
  repos: Repository[] = [];
  connections: Record<string, SandboxConnection> = {};
  loading = false;

  showCreate = false;
  createRepoId = '';
  createBranch = '';

  private pollInterval: ReturnType<typeof setInterval> | null = null;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.load();
    this.loadRepos();
    this.pollInterval = setInterval(() => this.load(), 10000);
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
  }

  load(): void {
    this.loading = true;
    this.api.listSandboxes().subscribe({
      next: (list) => { this.sandboxes = list; this.loading = false; },
      error: () => { this.loading = false; },
    });
  }

  loadRepos(): void {
    this.api.listRepositories('mine', 1, 100).subscribe({
      next: (r) => { this.repos = r.items; },
    });
  }

  doCreate(): void {
    this.api.createSandbox(this.createRepoId, this.createBranch).subscribe({
      next: () => { this.showCreate = false; this.createRepoId = ''; this.createBranch = ''; this.load(); },
    });
  }

  connect(sandbox: Sandbox): void {
    this.api.getSandboxConnection(sandbox.id).subscribe({
      next: (conn) => { this.connections[sandbox.id] = conn; },
    });
  }

  destroy(sandbox: Sandbox): void {
    if (confirm('Destroy this sandbox?')) {
      this.api.destroySandbox(sandbox.id).subscribe(() => this.load());
    }
  }
}
