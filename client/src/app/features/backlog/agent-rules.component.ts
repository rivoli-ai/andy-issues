import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';
import { ApiService, AgentRuleProfile } from '../../shared/services/api.service';

@Component({
  selector: 'app-agent-rules',
  imports: [CommonModule, FormsModule],
  template: `
    <section aria-labelledby="agent-rules-title">
      <h2 id="agent-rules-title">Agent rules</h2>
      <p>Named instructions for this repository. Stories inherit the default unless you select another profile. Repository owners can edit profiles.</p>
      <p *ngIf="loading" role="status">Loading profiles…</p>
      <p *ngIf="error" role="alert">{{ error }}</p>
      <p *ngIf="notice" role="status">{{ notice }}</p>
      <fieldset [disabled]="loading || saving || !canEdit">
        <div *ngFor="let profile of profiles; let i = index" class="profile">
          <label [for]="'rule-name-' + i">Profile name</label>
          <input [id]="'rule-name-' + i" [(ngModel)]="profile.name" maxlength="120" required />
          <button type="button" (click)="setDefault(i)" [attr.aria-pressed]="profile.isDefault">{{ profile.isDefault ? 'Default' : 'Make default' }}</button>
          <button type="button" (click)="move(i, -1)" [disabled]="i === 0" [attr.aria-label]="'Move ' + profile.name + ' up'">↑</button>
          <button type="button" (click)="move(i, 1)" [disabled]="i === profiles.length - 1" [attr.aria-label]="'Move ' + profile.name + ' down'">↓</button>
          <button type="button" (click)="remove(i)">Remove {{ profile.name }}</button>
          <label [for]="'rule-body-' + i">Instructions (Markdown)</label>
          <textarea [id]="'rule-body-' + i" [(ngModel)]="profile.body" rows="8" maxlength="65536"></textarea>
          <details><summary>Preview {{ profile.name }}</summary><div class="preview" [innerHTML]="preview(profile.body)"></div></details>
        </div>
        <button type="button" (click)="add()" [disabled]="profiles.length >= 100">Add profile</button>
        <button type="button" (click)="save()" [disabled]="!valid">{{ saving ? 'Saving…' : 'Save profiles' }}</button>
        <button type="button" (click)="load()">Discard changes</button>
      </fieldset>
      <p>Changes, including removals, take effect when you save. Removing a profile makes its stories inherit the default.</p>
    </section>
  `,
  styles: [`
    section { background: var(--surface); border: 1px solid var(--border); padding: 16px; border-radius: 8px; margin: 16px 0; }
    fieldset { border: 0; padding: 0; min-width: 0; }
    .profile { border-bottom: 1px solid var(--border); padding: 12px 0; }
    label { display: block; margin: 8px 0 4px; }
    textarea { display: block; width: 100%; box-sizing: border-box; font: inherit; }
    input, textarea { background: var(--background); color: var(--text); border: 1px solid var(--border); padding: 8px; }
    button { margin: 4px; padding: 6px 10px; }
    .preview { overflow-wrap: anywhere; overflow: auto; max-height: 400px; }
    [role=alert] { color: var(--error); }
  `],
})
export class AgentRulesComponent implements OnChanges {
  @Input({ required: true }) repositoryId = '';
  @Output() saved = new EventEmitter<AgentRuleProfile[]>();
  profiles: AgentRuleProfile[] = [];
  canEdit = false;
  loading = false;
  saving = false;
  error = '';
  notice = '';
  constructor(private api: ApiService) {}
  ngOnChanges(): void { this.load(); }
  load(): void {
    if (!this.repositoryId) return;
    this.loading = true;
    this.error = '';
    this.api.getAgentRules(this.repositoryId).subscribe({
      next: result => { this.profiles = result.profiles || []; this.canEdit = result.canEdit; this.loading = false; },
      error: () => { this.error = 'Unable to load profiles.'; this.loading = false; },
    });
  }
  add(): void {
    let name = 'New profile';
    let suffix = 2;
    while (this.profiles.some(p => p.name.toLowerCase() === name.toLowerCase())) name = `New profile ${suffix++}`;
    this.profiles.push({ name, body: '', isDefault: this.profiles.length === 0, sortOrder: this.profiles.length });
  }
  setDefault(index: number): void { this.profiles.forEach((p, i) => p.isDefault = i === index); }
  move(index: number, direction: number): void {
    const target = index + direction;
    if (target < 0 || target >= this.profiles.length) return;
    [this.profiles[index], this.profiles[target]] = [this.profiles[target], this.profiles[index]];
  }
  remove(index: number): void {
    this.profiles.splice(index, 1);
    if (this.profiles.length && !this.profiles.some(p => p.isDefault)) this.setDefault(this.profiles.length - 1);
  }
  get valid(): boolean {
    const names = this.profiles.map(p => p.name.trim().toLowerCase());
    return this.profiles.every(p => p.name.trim().length > 0 && p.body.length <= 65536) && new Set(names).size === names.length;
  }
  preview(body: string): string {
    // Angular sanitizes this plain string at the innerHTML binding; never bypass sanitization.
    return marked.parse(body, { async: false });
  }
  save(): void {
    if (!this.canEdit || !this.valid || this.saving) return;
    this.saving = true; this.error = ''; this.notice = '';
    const request = this.profiles.map((p, i) => ({ ...p, name: p.name.trim(), sortOrder: i }));
    this.api.replaceAgentRules(this.repositoryId, request).subscribe({
      next: profiles => { this.profiles = profiles; this.saving = false; this.notice = 'Profiles saved.'; this.saved.emit(profiles); },
      error: e => { this.saving = false; this.error = e.status === 404 ? 'Only the repository owner can save profiles.' : e.error?.error || 'Unable to save profiles. Your changes are still here.'; },
    });
  }
}
