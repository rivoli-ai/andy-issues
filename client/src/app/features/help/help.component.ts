// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MarkdownComponent } from '../../shared/ui/markdown.component';
import { environment } from '../../../environments/environment';

interface HelpTopicSummary {
  slug: string;
  title: string;
  order: number;
  tags: string[];
}

interface HelpTopic extends HelpTopicSummary {
  markdown: string;
}

@Component({
  selector: 'app-help',
  imports: [CommonModule, MarkdownComponent],
  template: `
    <div class="help-layout">
      <aside class="help-sidebar">
        <h2>Help Topics</h2>
        <input
          class="search-input"
          placeholder="Search help..."
          (input)="onSearch($event)"
        />
        <nav>
          <a
            *ngFor="let topic of topics"
            [class.active]="selectedSlug === topic.slug"
            (click)="selectTopic(topic.slug)"
          >
            {{ topic.title }}
          </a>
        </nav>
        <div class="help-meta">
          <p>
            <a [href]="swaggerUrl" target="_blank">Swagger API Docs</a>
          </p>
          <p class="copyright">Copyright &copy; Rivoli AI 2026</p>
        </div>
      </aside>

      <div class="help-content">
        <div *ngIf="loading" class="loading">Loading...</div>
        <div *ngIf="error" class="error">{{ error }}</div>
        <app-markdown *ngIf="currentTopic && !loading" [source]="currentTopic.markdown"></app-markdown>
        <div *ngIf="!currentTopic && !loading && !error" class="empty">
          Select a topic from the sidebar.
        </div>
      </div>
    </div>
  `,
  styles: [`
    .help-layout { display: flex; gap: 0; min-height: calc(100vh - 100px); margin: -24px; }

    .help-sidebar {
      width: 260px; min-width: 260px;
      background: var(--surface); border-right: 1px solid var(--border);
      padding: 24px 16px; display: flex; flex-direction: column;
    }
    .help-sidebar h2 { font-size: 16px; margin-bottom: 12px; }
    .search-input {
      width: 100%; padding: 8px 12px; border: 1px solid var(--border);
      border-radius: 4px; font-size: 13px; margin-bottom: 12px;
    }
    .help-sidebar nav { flex: 1; display: flex; flex-direction: column; gap: 2px; }
    .help-sidebar nav a {
      padding: 8px 12px; border-radius: 4px; font-size: 14px; cursor: pointer;
      color: var(--text-secondary);
    }
    .help-sidebar nav a:hover { background: var(--background); }
    .help-sidebar nav a.active { color: var(--primary); background: rgba(26,115,232,0.08); font-weight: 500; }
    .help-meta { margin-top: auto; padding-top: 16px; font-size: 12px; color: var(--text-secondary); }
    .help-meta a { font-size: 12px; }
    .copyright { margin-top: 8px; }

    .help-content { flex: 1; padding: 32px 40px; overflow-y: auto; }
    .loading, .error, .empty { color: var(--text-secondary); font-size: 14px; padding: 24px; }
    .error { color: var(--error); }

  `],
})
export class HelpComponent implements OnInit {
  topics: HelpTopicSummary[] = [];
  currentTopic: HelpTopic | null = null;
  selectedSlug = '';
  loading = false;
  error = '';
  swaggerUrl = '/swagger';

  private apiBase = environment.apiUrl;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadTopics();
  }

  loadTopics(): void {
    this.http.get<HelpTopicSummary[]>(`${this.apiBase}/help/topics`).subscribe({
      next: (topics) => {
        this.topics = topics;
        if (topics.length > 0) {
          this.selectTopic(topics[0].slug);
        }
      },
      error: () => {
        this.error = 'Could not load help topics.';
      },
    });
  }

  selectTopic(slug: string): void {
    this.selectedSlug = slug;
    this.loading = true;
    this.error = '';
    this.http.get<HelpTopic>(`${this.apiBase}/help/topics/${slug}`).subscribe({
      next: (topic) => {
        this.currentTopic = topic;
        this.loading = false;
      },
      error: () => {
        this.error = `Could not load topic: ${slug}`;
        this.loading = false;
      },
    });
  }

  onSearch(event: Event): void {
    const query = (event.target as HTMLInputElement).value;
    if (!query.trim()) {
      this.loadTopics();
      return;
    }
    this.http.get<HelpTopicSummary[]>(`${this.apiBase}/help/search`, { params: { q: query } }).subscribe({
      next: (topics) => {
        this.topics = topics;
      },
    });
  }

}
