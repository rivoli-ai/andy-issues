// Copyright (c) Rivoli AI 2026. All rights reserved.
import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { ApiService, Backlog, UserStory, AgentRuleProfile } from '../../shared/services/api.service';
import { AgentRulesComponent } from './agent-rules.component';
import { MarkdownComponent } from '../../shared/ui/markdown.component';
import { DialogDirective } from '../../shared/ui/dialog.directive';

@Component({
  selector: 'app-backlog',
  imports: [MarkdownComponent, AgentRulesComponent, DialogDirective, CommonModule, FormsModule, RouterLink],
  templateUrl: './backlog.component.html',
  styleUrls: ['./backlog.component.css'],
})
export class BacklogComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private requests = new Subscription();
  private loadVersion = 0;
  repositoryId: string | null = null;
  repositoryName = '';
  backlog: Backlog | null = null;
  loading = false;
  loadFailed = false;
  error = '';
  createError = '';
  pendingCreate: 'epic' | 'feature' | 'story' | null = null;
  generating = false;
  showRules = false;
  ruleProfiles: AgentRuleProfile[] = [];
  newStoryRuleId: string | null = null;
  pendingRules = new Set<string>();
  pendingStatuses = new Set<string>();
  pendingRefinements = new Set<string>();
  storyErrors = new Map<string, string>();
  showAddEpic = false;
  newEpicTitle = '';
  newEpicDesc = '';
  addFeatureEpicId: string | null = null;
  newFeatureTitle = '';
  addStoryFeatureId: string | null = null;
  newStoryTitle = '';
  newStoryDesc = '';
  newStoryPoints: number | null = null;

  constructor(private api: ApiService, private route: ActivatedRoute) {
    this.destroyRef.onDestroy(() => this.requests.unsubscribe());
  }
  ngOnInit(): void {
    this.route.params.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.requests.unsubscribe(); this.requests = new Subscription();
      this.repositoryId = params['repoId'] || null;
      this.repositoryName = ''; this.backlog = null; this.ruleProfiles = [];
      this.loading = false; this.error = ''; this.loadFailed = false; this.generating = false;
      this.pendingCreate = null; this.closeCreate(); this.showRules = false;
      this.newEpicTitle = ''; this.newEpicDesc = ''; this.newFeatureTitle = '';
      this.newStoryTitle = ''; this.newStoryDesc = ''; this.newStoryPoints = null; this.newStoryRuleId = null;
      this.pendingRules.clear(); this.pendingStatuses.clear(); this.pendingRefinements.clear(); this.storyErrors.clear();
      if (!this.repositoryId) return;
      this.load();
      this.requests.add(this.api.getRepository(this.repositoryId).subscribe({ next: r => this.repositoryName = r.name, error: () => {} }));
      this.requests.add(this.api.listAgentRules(this.repositoryId).subscribe({ next: p => this.ruleProfiles = p, error: () => this.error = 'Failed to load agent-rule profiles. Refresh the page to retry.' }));
    });
  }
  get featureParentTitle(): string { return this.backlog?.epics.find(e => e.id === this.addFeatureEpicId)?.title ?? ''; }
  get storyParentTitle(): string {
    for (const epic of this.backlog?.epics ?? []) {
      const feature = epic.features.find(f => f.id === this.addStoryFeatureId);
      if (feature) return epic.title + ' → ' + feature.title;
    }
    return '';
  }
  openEpic(): void { this.createError = ''; this.showAddEpic = true; }
  openFeature(id: string): void { this.createError = ''; this.addFeatureEpicId = id; }
  openStory(id: string): void { this.createError = ''; this.addStoryFeatureId = id; }
  closeCreate(): void {
    if (this.pendingCreate) return;
    this.showAddEpic = false; this.addFeatureEpicId = null; this.addStoryFeatureId = null; this.createError = '';
  }
  load(): void {
    if (!this.repositoryId) return;
    const version = ++this.loadVersion;
    this.loading = true; this.loadFailed = false; this.error = '';
    this.requests.add(this.api.getBacklog(this.repositoryId).subscribe({
      next: b => { if (version === this.loadVersion) { this.backlog = b; this.loading = false; } },
      error: e => { if (version === this.loadVersion) { this.loading = false; this.loadFailed = true; this.error = e.error?.error || 'Failed to load backlog.'; } },
    }));
  }
  addEpic(): void {
    if (!this.repositoryId || this.pendingCreate || !this.newEpicTitle.trim()) return;
    this.pendingCreate = 'epic'; this.createError = '';
    this.requests.add(this.api.createEpic(this.repositoryId, this.newEpicTitle.trim(), this.newEpicDesc || undefined).subscribe({
      next: () => { this.pendingCreate = null; this.closeCreate(); this.newEpicTitle = ''; this.newEpicDesc = ''; this.load(); },
      error: e => { this.pendingCreate = null; this.createError = e.error?.error || 'Failed to create epic. Your text has been kept.'; },
    }));
  }
  addFeature(): void {
    if (!this.addFeatureEpicId || this.pendingCreate || !this.newFeatureTitle.trim()) return;
    this.pendingCreate = 'feature'; this.createError = '';
    this.requests.add(this.api.createFeature(this.addFeatureEpicId, this.newFeatureTitle.trim()).subscribe({
      next: () => { this.pendingCreate = null; this.closeCreate(); this.newFeatureTitle = ''; this.load(); },
      error: e => { this.pendingCreate = null; this.createError = e.error?.error || 'Failed to create feature. Your text has been kept.'; },
    }));
  }
  addStory(): void {
    if (!this.addStoryFeatureId || this.pendingCreate || !this.newStoryTitle.trim()) return;
    if (this.newStoryPoints !== null && (!Number.isInteger(this.newStoryPoints) || this.newStoryPoints < 0)) { this.createError = 'Story points must be a whole number of zero or more.'; return; }
    this.pendingCreate = 'story'; this.createError = '';
    this.requests.add(this.api.createStory(this.addStoryFeatureId, this.newStoryTitle.trim(), this.newStoryDesc || undefined, undefined, this.newStoryPoints ?? undefined, this.newStoryRuleId).subscribe({
      next: () => { this.pendingCreate = null; this.closeCreate(); this.newStoryTitle = ''; this.newStoryDesc = ''; this.newStoryPoints = null; this.newStoryRuleId = null; this.load(); },
      error: e => { this.pendingCreate = null; this.createError = e.error?.error || 'Failed to create story. Your text has been kept.'; },
    }));
  }
  rulesSaved(profiles: AgentRuleProfile[]): void { this.ruleProfiles = profiles; this.load(); }
  setRule(story: UserStory, ruleId: string | null): void {
    if (this.pendingRules.has(story.id)) return;
    this.pendingRules.add(story.id); this.storyErrors.delete(story.id);
    this.requests.add(this.api.selectAgentRule(story.id, ruleId).subscribe({
      next: () => { story.agentRuleId = ruleId; this.pendingRules.delete(story.id); },
      error: e => { this.storyErrors.set(story.id, e.error?.error || 'Failed to update agent rules. The previous choice is unchanged.'); this.pendingRules.delete(story.id); },
    }));
  }
  setStatus(story: UserStory, status: string): void {
    if (this.pendingStatuses.has(story.id)) return;
    this.pendingStatuses.add(story.id); this.storyErrors.delete(story.id);
    this.requests.add(this.api.updateStoryStatus(story.id, status).subscribe({
      next: updated => { story.status = updated.status; this.pendingStatuses.delete(story.id); },
      error: e => { this.storyErrors.set(story.id, e.error?.error || 'Failed to update status. The previous status is unchanged.'); this.pendingStatuses.delete(story.id); },
    }));
  }
  refine(story: UserStory): void {
    if (this.pendingRefinements.has(story.id) || story.triageState?.kind === 'Triaging') return;
    this.pendingRefinements.add(story.id); this.storyErrors.delete(story.id);
    this.requests.add(this.api.refineStory(story.id).subscribe({
      next: run => { story.triageState = { kind: 'Triaging', refineRunId: run.refineRunId }; this.pendingRefinements.delete(story.id); },
      error: e => { this.storyErrors.set(story.id, e.error?.error || 'Could not start refinement. Try again.'); this.pendingRefinements.delete(story.id); },
    }));
  }
  generateDraft(): void {
    if (!this.repositoryId || this.generating) return;
    this.generating = true; this.error = '';
    this.requests.add(this.api.generateDraftBacklog(this.repositoryId).subscribe({
      next: () => { this.generating = false; this.load(); },
      error: e => { this.generating = false; this.error = e.error?.error || 'Generation failed. Try again.'; },
    }));
  }
}
