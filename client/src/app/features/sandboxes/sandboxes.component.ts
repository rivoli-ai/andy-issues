// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { DialogDirective } from '../../shared/ui/dialog.directive';
import {
  ApiService,
  SandboxSummary,
  SandboxConnection,
  Repository,
} from '../../shared/services/api.service';

@Component({
  selector: 'app-sandboxes',
  imports: [CommonModule, FormsModule, DialogDirective],
  template: `
    <div class="page-header">
      <h1>Sandboxes <small aria-live="polite">{{ capacity.current }}/{{ capacity.max }}</small></h1>
      <div class="card-actions">
        <button class="btn-secondary" (click)="showCloseAll = true" [disabled]="pending || loading || sandboxes.length === 0">Close all mine</button>
        <button class="btn-primary" (click)="showCreate = true" [disabled]="pending || loading || capacity.current >= capacity.max">Create Sandbox</button>
      </div>
    </div>

    <p role="alert" *ngIf="error">{{ error }}</p>
    <ul *ngIf="closeFailures.length" aria-label="Sandboxes that could not be closed">
      <li *ngFor="let failure of closeFailures">{{ failure.id }}: {{ failure.reason }}</li>
    </ul>
    <div class="modal-backdrop" *ngIf="showCloseAll" (click)="dismissCloseAll()">
      <div class="modal" appDialog aria-labelledby="close-all-title" (dialogDismiss)="dismissCloseAll()" (click)="$event.stopPropagation()">
        <h2 id="close-all-title">Close all your sandboxes?</h2>
        <p role="alert" *ngIf="error">{{ error }}</p>
        <p>This destroys your {{ sandboxes.length }} environments. Save your work first.</p>
        <div class="modal-actions">
          <button class="btn-secondary" [disabled]="pending" (click)="dismissCloseAll()">Cancel</button>
          <button class="btn-primary" [disabled]="pending" (click)="closeAll()">{{ pending ? 'Closing…' : 'Close all mine' }}</button>
        </div>
      </div>
    </div>

    <!-- Create modal -->
    <div class="modal-backdrop" *ngIf="showCreate" (click)="dismissCreate()">
      <div class="modal" appDialog aria-labelledby="create-sandbox-title" (dialogDismiss)="dismissCreate()" (click)="$event.stopPropagation()">
        <h2 id="create-sandbox-title">Create Sandbox</h2>
        <p role="alert" *ngIf="error">{{ error }}</p>
        <label for="sandbox-repository">Repository</label>
        <select id="sandbox-repository" class="input" [(ngModel)]="createRepoId" [disabled]="pending">
          <option value="">Select repository...</option>
          <option *ngFor="let r of repos" [value]="r.id">{{ r.name }}</option>
        </select>
        <label for="sandbox-branch">Branch</label>
        <input id="sandbox-branch" class="input" [(ngModel)]="createBranch" [disabled]="pending" />
        <div class="modal-actions">
          <button class="btn-secondary" (click)="dismissCreate()" [disabled]="pending">Cancel</button>
          <button class="btn-primary" (click)="doCreate()" [disabled]="pending || !createRepoId || !createBranch.trim() || capacity.current >= capacity.max">{{ pending ? 'Creating…' : 'Create' }}</button>
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
          <p class="card-meta">{{ s.repositoryName }} · {{ s.purpose }}</p>
        </div>
        <div class="card-actions">
          <button class="btn-sm btn-secondary" (click)="connect(s)">Connect</button>
          <button class="btn-sm btn-secondary btn-danger" (click)="destroy(s)" [disabled]="pending">Destroy</button>
        </div>

        <!-- Connection info -->
        <div *ngIf="connections[s.id]" class="connection-info">
          <p *ngIf="connections[s.id].ideEndpoint">
            IDE: <a [href]="connections[s.id].ideEndpoint!" target="_blank" rel="noopener noreferrer">{{ connections[s.id].ideEndpoint }}</a>
          </p>
          <p *ngIf="connections[s.id].vncEndpoint">
            VNC: <a [href]="connections[s.id].vncEndpoint!" target="_blank" rel="noopener noreferrer">{{ connections[s.id].vncEndpoint }}</a>
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
  sandboxes: SandboxSummary[] = [];
  capacity = { current: 0, max: 0, tenantMax: 20 };
  pending = false;
  error = '';
  closeFailures: { id: string; reason: string }[] = [];
  showCloseAll = false;
  private readonly destroyed$ = new Subject<void>();
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
    this.destroyed$.next();
    this.destroyed$.complete();
  }

  load(): void {
    if (this.loading || this.pending) return;
    this.loading = true;
    this.api.listMySandboxes().pipe(takeUntil(this.destroyed$)).subscribe({
      next: (result) => { this.sandboxes = result.items; this.capacity = result.capacity; this.loading = false; },
      error: () => { this.loading = false; this.error = 'Could not load your sandboxes. Retrying shortly.'; },
    });
  }

  loadRepos(): void {
    this.api.listRepositories('mine', 1, 100).pipe(takeUntil(this.destroyed$)).subscribe({
      next: (r) => { this.repos = r.items; },
    });
  }

  dismissCreate(): void { if (!this.pending) this.showCreate = false; }
  dismissCloseAll(): void { if (!this.pending) this.showCloseAll = false; }

  doCreate(): void {
    if (this.pending || !this.createRepoId || !this.createBranch.trim()) return;
    this.pending = true;
    this.error = '';
    this.api.createSandbox(this.createRepoId, this.createBranch.trim()).pipe(takeUntil(this.destroyed$)).subscribe({
      next: () => { this.pending = false; this.showCreate = false; this.createRepoId = ''; this.createBranch = ''; this.load(); },
      error: (err) => { this.pending = false; this.error = err.status === 409 ? 'Sandbox capacity reached. Close an environment and retry.' : 'Could not create the sandbox. Your entries are preserved.'; this.load(); },
    });
  }

  closeAll(): void {
    if (this.pending || !this.showCloseAll) return;
    this.pending = true;
    this.error = '';
    this.closeFailures = [];
    this.api.closeAllMySandboxes().pipe(takeUntil(this.destroyed$)).subscribe({
      next: (result) => { this.pending = false; this.showCloseAll = false; this.closeFailures = result.failed; this.connections = {}; this.load(); },
      error: () => { this.pending = false; this.error = 'Could not close your sandboxes. Retry to close any remaining environments.'; },
    });
  }

  connect(sandbox: SandboxSummary): void {
    this.api.getSandboxConnection(sandbox.id).pipe(takeUntil(this.destroyed$)).subscribe({
      next: (conn) => { this.connections[sandbox.id] = conn; },
    });
  }

  destroy(sandbox: SandboxSummary): void {
    if (!this.pending && confirm('Destroy this sandbox?')) {
      this.pending = true;
      this.api.destroySandbox(sandbox.id).pipe(takeUntil(this.destroyed$)).subscribe({
        next: () => { this.pending = false; delete this.connections[sandbox.id]; this.load(); },
        error: () => { this.pending = false; this.error = 'Could not destroy this sandbox. Please retry.'; },
      });
    }
  }
}
