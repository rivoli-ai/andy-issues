// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ApiService,
  Backlog,
  Epic,
  Feature,
  UserStory,
} from '../../shared/services/api.service';

@Component({
  selector: 'app-backlog',
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="backlog-header">
      <h1>Backlog</h1>
      <div class="actions">
        <button class="btn-secondary" (click)="showAddEpic = true">Add Epic</button>
        <button class="btn-primary" (click)="generateDraft()" [disabled]="generating">
          {{ generating ? 'Generating...' : 'Generate Draft' }}
        </button>
      </div>
    </div>

    <p *ngIf="error" class="error-msg">{{ error }}</p>

    <div *ngIf="!repositoryId" class="empty">
      <p>Select a repository to view its backlog.</p>
      <a routerLink="/repositories">Go to Repositories</a>
    </div>

    <!-- Add Epic modal -->
    <div class="modal-backdrop" *ngIf="showAddEpic" (click)="showAddEpic = false">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>New Epic</h2>
        <input class="input" placeholder="Title" [(ngModel)]="newEpicTitle" />
        <textarea class="input textarea" placeholder="Description (optional)" [(ngModel)]="newEpicDesc" rows="3"></textarea>
        <div class="modal-actions">
          <button class="btn-secondary" (click)="showAddEpic = false">Cancel</button>
          <button class="btn-primary" (click)="addEpic()" [disabled]="!newEpicTitle.trim()">Create</button>
        </div>
      </div>
    </div>

    <!-- Add Feature modal -->
    <div class="modal-backdrop" *ngIf="addFeatureEpicId" (click)="addFeatureEpicId = null">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>New Feature</h2>
        <input class="input" placeholder="Title" [(ngModel)]="newFeatureTitle" />
        <div class="modal-actions">
          <button class="btn-secondary" (click)="addFeatureEpicId = null">Cancel</button>
          <button class="btn-primary" (click)="addFeature()" [disabled]="!newFeatureTitle.trim()">Create</button>
        </div>
      </div>
    </div>

    <!-- Add Story modal -->
    <div class="modal-backdrop" *ngIf="addStoryFeatureId" (click)="addStoryFeatureId = null">
      <div class="modal" (click)="$event.stopPropagation()">
        <h2>New Story</h2>
        <input class="input" placeholder="Title" [(ngModel)]="newStoryTitle" />
        <textarea class="input textarea" placeholder="Description (optional)" [(ngModel)]="newStoryDesc" rows="2"></textarea>
        <input class="input" placeholder="Story points" type="number" [(ngModel)]="newStoryPoints" />
        <div class="modal-actions">
          <button class="btn-secondary" (click)="addStoryFeatureId = null">Cancel</button>
          <button class="btn-primary" (click)="addStory()" [disabled]="!newStoryTitle.trim()">Create</button>
        </div>
      </div>
    </div>

    <!-- Backlog tree -->
    <div class="backlog-tree" *ngIf="backlog">
      <div *ngFor="let epic of backlog.epics" class="epic-card">
        <div class="epic-header">
          <h2>{{ epic.title }}</h2>
          <button class="btn-sm btn-secondary" (click)="addFeatureEpicId = epic.id">+ Feature</button>
        </div>
        <p *ngIf="epic.description" class="desc">{{ epic.description }}</p>

        <div *ngFor="let feature of epic.features" class="feature-card">
          <div class="feature-header">
            <h3>{{ feature.title }}</h3>
            <button class="btn-sm btn-secondary" (click)="addStoryFeatureId = feature.id">+ Story</button>
          </div>

          <div *ngFor="let story of feature.stories" class="story-row">
            <span class="story-status" [class]="'status-' + story.status.toLowerCase()">{{ story.status }}</span>
            <span class="story-title">{{ story.title }}</span>
            <span class="story-points" *ngIf="story.storyPoints">{{ story.storyPoints }}pts</span>
            <select class="status-select" [ngModel]="story.status" (ngModelChange)="setStatus(story, $event)">
              <option>Draft</option>
              <option>Ready</option>
              <option>InProgress</option>
              <option>Done</option>
            </select>
          </div>
          <p *ngIf="feature.stories.length === 0" class="empty-sm">No stories</p>
        </div>
        <p *ngIf="epic.features.length === 0" class="empty-sm">No features</p>
      </div>
      <p *ngIf="backlog.epics.length === 0" class="empty">No epics yet. Add one or generate a draft backlog.</p>
    </div>
  `,
  styles: [`
    .backlog-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
    .backlog-header h1 { margin: 0; }
    .actions { display: flex; gap: 8px; }
    .error-msg { color: var(--error); font-size: 13px; margin-bottom: 12px; }

    .backlog-tree { display: flex; flex-direction: column; gap: 16px; }
    .epic-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 16px; }
    .epic-header { display: flex; justify-content: space-between; align-items: center; }
    .epic-header h2 { font-size: 16px; margin: 0; }
    .desc { font-size: 13px; color: var(--text-secondary); margin: 4px 0 12px; }

    .feature-card { margin: 12px 0 0 16px; padding: 12px; background: var(--background); border-radius: 6px; }
    .feature-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    .feature-header h3 { font-size: 14px; margin: 0; }

    .story-row { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: 4px; font-size: 13px; }
    .story-row:hover { background: var(--surface); }
    .story-status { padding: 2px 8px; border-radius: 12px; font-size: 11px; font-weight: 500; min-width: 70px; text-align: center; }
    .status-draft { background: #fce8e6; color: var(--error); }
    .status-ready { background: #e8f0fe; color: var(--primary); }
    .status-inprogress { background: #fef7e0; color: #e37400; }
    .status-done { background: #e6f4ea; color: var(--success); }
    .story-title { flex: 1; }
    .story-points { font-size: 11px; color: var(--text-secondary); background: var(--background); padding: 2px 6px; border-radius: 4px; }
    .status-select { padding: 2px 4px; font-size: 11px; border: 1px solid var(--border); border-radius: 4px; background: transparent; }

    .btn-sm { padding: 4px 10px; font-size: 12px; }
    .empty { color: var(--text-secondary); font-size: 14px; padding: 24px 0; }
    .empty-sm { color: var(--text-secondary); font-size: 12px; padding: 4px 8px; }

    .modal-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.4); display: flex; align-items: center; justify-content: center; z-index: 100; }
    .modal { background: var(--surface); border-radius: 12px; padding: 24px; min-width: 400px; }
    .modal h2 { font-size: 18px; margin-bottom: 16px; }
    .modal .input { width: 100%; padding: 8px 12px; border: 1px solid var(--border); border-radius: 4px; font-size: 14px; margin-bottom: 12px; }
    .modal .textarea { resize: vertical; }
    .modal-actions { display: flex; gap: 8px; justify-content: flex-end; }
  `],
})
export class BacklogComponent implements OnInit {
  repositoryId: string | null = null;
  backlog: Backlog | null = null;
  error = '';
  generating = false;

  showAddEpic = false;
  newEpicTitle = '';
  newEpicDesc = '';

  addFeatureEpicId: string | null = null;
  newFeatureTitle = '';

  addStoryFeatureId: string | null = null;
  newStoryTitle = '';
  newStoryDesc = '';
  newStoryPoints: number | null = null;

  constructor(private api: ApiService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.route.params.subscribe((params) => {
      this.repositoryId = params['repoId'] || null;
      if (this.repositoryId) this.load();
    });
  }

  load(): void {
    if (!this.repositoryId) return;
    this.api.getBacklog(this.repositoryId).subscribe({
      next: (b) => { this.backlog = b; this.error = ''; },
      error: (e) => { this.error = e.error?.error || 'Failed to load backlog'; },
    });
  }

  addEpic(): void {
    if (!this.repositoryId) return;
    this.api.createEpic(this.repositoryId, this.newEpicTitle, this.newEpicDesc || undefined).subscribe({
      next: () => { this.showAddEpic = false; this.newEpicTitle = ''; this.newEpicDesc = ''; this.load(); },
      error: (e) => { this.error = e.error?.error || 'Failed to create epic'; },
    });
  }

  addFeature(): void {
    if (!this.addFeatureEpicId) return;
    this.api.createFeature(this.addFeatureEpicId, this.newFeatureTitle).subscribe({
      next: () => { this.addFeatureEpicId = null; this.newFeatureTitle = ''; this.load(); },
      error: (e) => { this.error = e.error?.error || 'Failed to create feature'; },
    });
  }

  addStory(): void {
    if (!this.addStoryFeatureId) return;
    this.api.createStory(
      this.addStoryFeatureId,
      this.newStoryTitle,
      this.newStoryDesc || undefined,
      undefined,
      this.newStoryPoints ?? undefined,
    ).subscribe({
      next: () => { this.addStoryFeatureId = null; this.newStoryTitle = ''; this.newStoryDesc = ''; this.newStoryPoints = null; this.load(); },
      error: (e) => { this.error = e.error?.error || 'Failed to create story'; },
    });
  }

  setStatus(story: UserStory, newStatus: string): void {
    this.api.updateStoryStatus(story.id, newStatus).subscribe({
      next: (updated) => { story.status = updated.status; },
      error: (e) => { this.error = e.error?.error || 'Failed to update status'; },
    });
  }

  generateDraft(): void {
    if (!this.repositoryId) return;
    this.generating = true;
    this.api.generateDraftBacklog(this.repositoryId).subscribe({
      next: () => { this.generating = false; this.load(); },
      error: (e) => { this.generating = false; this.error = e.error?.error || 'Generation failed'; },
    });
  }
}
