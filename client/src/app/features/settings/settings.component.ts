// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ApiService,
  LinkedProvider,
  McpServerConfig,
  ArtifactFeedConfig,
} from '../../shared/services/api.service';

@Component({
  selector: 'app-settings',
  imports: [CommonModule, FormsModule],
  template: `
    <h1>Settings</h1>

    <div class="tab-strip">
      <button *ngFor="let tab of tabs" [class.active]="activeTab === tab" (click)="activeTab = tab">{{ tab }}</button>
    </div>

    <!-- Source Control tab -->
    <div *ngIf="activeTab === 'Source Control'" class="tab-panel">
      <h2>Linked Providers</h2>
      <table class="data-table" *ngIf="providers.length > 0">
        <thead><tr><th>Provider</th><th>Account</th><th>Linked</th><th>Actions</th></tr></thead>
        <tbody>
          <tr *ngFor="let p of providers">
            <td><span class="badge badge-provider">{{ p.provider }}</span></td>
            <td>{{ p.accountLogin || '-' }}</td>
            <td>{{ p.createdAt | date:'short' }}</td>
            <td><button class="btn-sm btn-secondary btn-danger" (click)="unlinkProvider(p)">Unlink</button></td>
          </tr>
        </tbody>
      </table>
      <p *ngIf="providers.length === 0" class="empty">No linked providers.</p>

      <div class="link-form">
        <h3>Link via PAT</h3>
        <select class="input input-sm" [(ngModel)]="patProvider">
          <option value="github">GitHub</option>
          <option value="azuredevops">Azure DevOps</option>
        </select>
        <input class="input input-sm" type="password" placeholder="Personal Access Token" [(ngModel)]="patValue" />
        <button class="btn-primary btn-sm" (click)="linkPat()" [disabled]="!patValue.trim()">Link</button>
      </div>
    </div>

    <!-- MCP tab -->
    <div *ngIf="activeTab === 'MCP'" class="tab-panel">
      <h2>MCP Server Configurations</h2>
      <table class="data-table" *ngIf="mcpConfigs.length > 0">
        <thead><tr><th>Name</th><th>Type</th><th>Scope</th><th>Enabled</th><th>Actions</th></tr></thead>
        <tbody>
          <tr *ngFor="let m of mcpConfigs">
            <td>{{ m.name }}</td>
            <td>{{ m.type }}</td>
            <td>{{ m.isShared ? 'shared' : 'personal' }}</td>
            <td>
              <button class="btn-sm" [class.btn-primary]="m.enabled" [class.btn-secondary]="!m.enabled" (click)="toggleMcp(m)">
                {{ m.enabled ? 'Enabled' : 'Disabled' }}
              </button>
            </td>
            <td><button class="btn-sm btn-secondary btn-danger" (click)="deleteMcp(m)">Delete</button></td>
          </tr>
        </tbody>
      </table>
      <p *ngIf="mcpConfigs.length === 0" class="empty">No MCP configurations.</p>

      <div class="add-form">
        <h3>Add stdio server</h3>
        <input class="input input-sm" placeholder="Name" [(ngModel)]="newMcpName" />
        <input class="input input-sm" placeholder="Command" [(ngModel)]="newMcpCommand" />
        <label><input type="checkbox" [(ngModel)]="newMcpShared" /> Shared</label>
        <button class="btn-primary btn-sm" (click)="addMcp()" [disabled]="!newMcpName.trim() || !newMcpCommand.trim()">Add</button>
      </div>
    </div>

    <!-- Artifact Feeds tab -->
    <div *ngIf="activeTab === 'Artifact Feeds'" class="tab-panel">
      <h2>Artifact Feeds (Admin)</h2>
      <table class="data-table" *ngIf="feeds.length > 0">
        <thead><tr><th>Name</th><th>Type</th><th>Organization</th><th>Feed</th><th>Enabled</th><th>Actions</th></tr></thead>
        <tbody>
          <tr *ngFor="let f of feeds">
            <td>{{ f.name }}</td>
            <td>{{ f.type }}</td>
            <td>{{ f.organization }}</td>
            <td>{{ f.feedName }}</td>
            <td>{{ f.enabled ? 'Yes' : 'No' }}</td>
            <td><button class="btn-sm btn-secondary btn-danger" (click)="deleteFeed(f)">Delete</button></td>
          </tr>
        </tbody>
      </table>
      <p *ngIf="feeds.length === 0" class="empty">No artifact feeds configured.</p>

      <div class="add-form">
        <h3>Add Feed</h3>
        <input class="input input-sm" placeholder="Name" [(ngModel)]="newFeedName" />
        <input class="input input-sm" placeholder="Organization" [(ngModel)]="newFeedOrg" />
        <input class="input input-sm" placeholder="Feed name" [(ngModel)]="newFeedFeedName" />
        <select class="input input-sm" [(ngModel)]="newFeedType">
          <option value="nuget">NuGet</option>
          <option value="npm">npm</option>
        </select>
        <button class="btn-primary btn-sm" (click)="addFeed()" [disabled]="!newFeedName.trim() || !newFeedOrg.trim() || !newFeedFeedName.trim()">Add</button>
      </div>
    </div>
  `,
  styles: [`
    h1 { margin-bottom: 20px; }
    .tab-strip { display: flex; gap: 0; border-bottom: 2px solid var(--border); margin-bottom: 24px; }
    .tab-strip button {
      padding: 10px 20px; border: none; background: transparent; font-size: 14px; font-weight: 500;
      color: var(--text-secondary); cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -2px;
    }
    .tab-strip button.active { color: var(--primary); border-bottom-color: var(--primary); }
    .tab-strip button:hover { color: var(--text); }

    .tab-panel h2 { font-size: 16px; margin-bottom: 16px; }
    .tab-panel h3 { font-size: 14px; margin: 24px 0 12px; }

    .data-table { width: 100%; border-collapse: collapse; background: var(--surface); border-radius: 8px; overflow: hidden; margin-bottom: 16px; }
    th, td { padding: 10px 14px; text-align: left; border-bottom: 1px solid var(--border); font-size: 13px; }
    th { background: var(--background); font-weight: 600; color: var(--text-secondary); font-size: 12px; text-transform: uppercase; }
    .badge { padding: 2px 8px; border-radius: 12px; font-size: 11px; font-weight: 500; }
    .badge-provider { background: #e8f0fe; color: var(--primary); }
    .btn-sm { padding: 4px 10px; font-size: 12px; }
    .btn-danger { color: var(--error); border-color: var(--error); }
    .empty { color: var(--text-secondary); font-size: 13px; margin-bottom: 16px; }

    .link-form, .add-form { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .input-sm { padding: 6px 10px; border: 1px solid var(--border); border-radius: 4px; font-size: 13px; }
    label { font-size: 13px; display: flex; align-items: center; gap: 4px; }
  `],
})
export class SettingsComponent implements OnInit {
  tabs = ['Source Control', 'MCP', 'Artifact Feeds'];
  activeTab = 'Source Control';

  providers: LinkedProvider[] = [];
  mcpConfigs: McpServerConfig[] = [];
  feeds: ArtifactFeedConfig[] = [];

  patProvider = 'github';
  patValue = '';

  newMcpName = '';
  newMcpCommand = '';
  newMcpShared = false;

  newFeedName = '';
  newFeedOrg = '';
  newFeedFeedName = '';
  newFeedType = 'nuget';

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadProviders();
    this.loadMcp();
    this.loadFeeds();
  }

  // ── Source Control ────────────────────────────────────────────

  loadProviders(): void {
    this.api.listLinkedProviders().subscribe({ next: (p) => { this.providers = p; } });
  }

  linkPat(): void {
    this.api.linkPat(this.patProvider, this.patValue).subscribe({
      next: () => { this.patValue = ''; this.loadProviders(); },
    });
  }

  unlinkProvider(p: LinkedProvider): void {
    if (confirm(`Unlink ${p.provider}?`)) {
      this.api.unlinkProvider(p.id).subscribe(() => this.loadProviders());
    }
  }

  // ── MCP ───────────────────────────────────────────────────────

  loadMcp(): void {
    this.api.listMcpConfigs().subscribe({ next: (m) => { this.mcpConfigs = m; } });
  }

  addMcp(): void {
    this.api.createMcpConfig({
      name: this.newMcpName,
      type: 'stdio',
      command: this.newMcpCommand,
      isShared: this.newMcpShared || undefined,
    }).subscribe({
      next: () => { this.newMcpName = ''; this.newMcpCommand = ''; this.newMcpShared = false; this.loadMcp(); },
    });
  }

  toggleMcp(m: McpServerConfig): void {
    this.api.toggleMcpConfig(m.id).subscribe({ next: (updated) => { m.enabled = updated.enabled; } });
  }

  deleteMcp(m: McpServerConfig): void {
    if (confirm(`Delete "${m.name}"?`)) {
      this.api.deleteMcpConfig(m.id).subscribe(() => this.loadMcp());
    }
  }

  // ── Artifact Feeds ────────────────────────────────────────────

  loadFeeds(): void {
    this.api.listAllFeeds().subscribe({ next: (f) => { this.feeds = f; } });
  }

  addFeed(): void {
    this.api.createFeed({
      name: this.newFeedName,
      organization: this.newFeedOrg,
      feedName: this.newFeedFeedName,
      type: this.newFeedType,
    }).subscribe({
      next: () => {
        this.newFeedName = ''; this.newFeedOrg = ''; this.newFeedFeedName = '';
        this.loadFeeds();
      },
    });
  }

  deleteFeed(f: ArtifactFeedConfig): void {
    if (confirm(`Delete "${f.name}"?`)) {
      this.api.deleteFeed(f.id).subscribe(() => this.loadFeeds());
    }
  }
}
