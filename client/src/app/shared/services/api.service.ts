// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// ── Repository ──────────────────────────────────────────────────────

export interface Repository {
  id: string;
  ownerUserId: string;
  name: string;
  description: string | null;
  provider: string;
  cloneUrl: string;
  defaultBranch: string;
  externalId: string | null;
  llmSettingId: string | null;
  hasAzureIdentity: boolean;
  codeIndexStatus: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface RepositoryShare {
  id: string;
  repositoryId: string;
  sharedWithUserId: string;
  grantedByUserId: string;
  grantedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface SyncResult {
  added: number;
  updated: number;
  skipped: number;
  errors: string[];
}

// ── Backlog ─────────────────────────────────────────────────────────

export interface Epic {
  id: string;
  repositoryId: string;
  title: string;
  description: string | null;
  order: number;
  externalId: string | null;
  createdAt: string;
  updatedAt: string | null;
  features: Feature[];
}

export interface Feature {
  id: string;
  epicId: string;
  title: string;
  description: string | null;
  order: number;
  externalId: string | null;
  createdAt: string;
  updatedAt: string | null;
  stories: UserStory[];
}

export interface UserStory {
  id: string;
  featureId: string;
  title: string;
  description: string | null;
  acceptanceCriteria: string | null;
  storyPoints: number | null;
  status: string;
  pullRequestUrl: string | null;
  order: number;
  externalId: string | null;
  azureDevOpsWorkItemId: number | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface Backlog {
  repositoryId: string;
  epics: Epic[];
}

// ── Sandbox ─────────────────────────────────────────────────────────

export interface Sandbox {
  id: string;
  containerId: string;
  repositoryId: string;
  ownerUserId: string;
  branch: string;
  status: string;
  ideEndpoint: string | null;
  vncEndpoint: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface SandboxConnection {
  ideEndpoint: string | null;
  vncEndpoint: string | null;
  sshEndpoint: string | null;
}

// ── MCP Config ──────────────────────────────────────────────────────

export interface McpServerConfig {
  id: string;
  ownerUserId: string | null;
  isShared: boolean;
  name: string;
  description: string | null;
  type: string;
  enabled: boolean;
  command: string | null;
  argumentsJson: string | null;
  url: string | null;
  hasEnvironment: boolean;
  hasHeaders: boolean;
  createdAt: string;
  updatedAt: string | null;
}

// ── Artifact Feed ───────────────────────────────────────────────────

export interface ArtifactFeedConfig {
  id: string;
  name: string;
  organization: string;
  feedName: string;
  project: string | null;
  type: string;
  enabled: boolean;
  createdAt: string;
  updatedAt: string | null;
}

// ── Linked Provider ─────────────────────────────────────────────────

export interface LinkedProvider {
  id: string;
  provider: string;
  accountLogin: string | null;
  expiresAt: string | null;
  createdAt: string;
  updatedAt: string | null;
}

// ── User ────────────────────────────────────────────────────────────

export interface UserSuggestion {
  userId: string;
  email: string;
  displayName: string | null;
}

// ── Service ─────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class ApiService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // ── Repositories ──────────────────────────────────────────────

  listRepositories(scope = 'mine', page = 1, pageSize = 20): Observable<PagedResult<Repository>> {
    const params = new HttpParams()
      .set('scope', scope)
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<PagedResult<Repository>>(`${this.baseUrl}/repositories`, { params });
  }

  getRepository(id: string): Observable<Repository> {
    return this.http.get<Repository>(`${this.baseUrl}/repositories/${id}`);
  }

  deleteRepository(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/repositories/${id}`);
  }

  shareRepository(id: string, email: string): Observable<RepositoryShare> {
    return this.http.post<RepositoryShare>(`${this.baseUrl}/repositories/${id}/share`, { email });
  }

  listShares(id: string): Observable<RepositoryShare[]> {
    return this.http.get<RepositoryShare[]>(`${this.baseUrl}/repositories/${id}/shares`);
  }

  syncGitHub(repoIds: string[]): Observable<SyncResult> {
    return this.http.post<SyncResult>(`${this.baseUrl}/repositories/sync-github`, { repoIds });
  }

  syncAzureDevOps(organization: string, repoIds: string[], project?: string): Observable<SyncResult> {
    return this.http.post<SyncResult>(`${this.baseUrl}/repositories/sync-azure`, { organization, project, repoIds });
  }

  setLlmSetting(repoId: string, llmSettingId: string | null): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/repositories/${repoId}/llm-setting`, { llmSettingId });
  }

  setAzureIdentity(repoId: string, body: { clientId: string; clientSecret: string; tenantId: string; subscriptionId?: string }): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/repositories/${repoId}/azure-identity`, body);
  }

  verifyAzureIdentity(repoId: string): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.baseUrl}/repositories/${repoId}/verify-azure-identity`, {});
  }

  // ── Backlog ───────────────────────────────────────────────────

  getBacklog(repositoryId: string): Observable<Backlog> {
    return this.http.get<Backlog>(`${this.baseUrl}/repositories/${repositoryId}/backlog`);
  }

  createEpic(repositoryId: string, title: string, description?: string): Observable<Epic> {
    return this.http.post<Epic>(`${this.baseUrl}/repositories/${repositoryId}/epics`, { title, description });
  }

  createFeature(epicId: string, title: string, description?: string): Observable<Feature> {
    return this.http.post<Feature>(`${this.baseUrl}/epics/${epicId}/features`, { title, description });
  }

  createStory(featureId: string, title: string, description?: string, acceptanceCriteria?: string, storyPoints?: number): Observable<UserStory> {
    return this.http.post<UserStory>(`${this.baseUrl}/features/${featureId}/stories`, { title, description, acceptanceCriteria, storyPoints });
  }

  updateStoryStatus(storyId: string, status: string, pullRequestUrl?: string): Observable<UserStory> {
    return this.http.patch<UserStory>(`${this.baseUrl}/stories/${storyId}/status`, { status, pullRequestUrl });
  }

  generateDraftBacklog(repositoryId: string): Observable<Backlog> {
    return this.http.post<Backlog>(`${this.baseUrl}/repositories/${repositoryId}/generate-backlog`, {});
  }

  // ── Sandboxes ─────────────────────────────────────────────────

  createSandbox(repositoryId: string, branch: string): Observable<Sandbox> {
    return this.http.post<Sandbox>(`${this.baseUrl}/sandboxes`, { repositoryId, branch });
  }

  listSandboxes(): Observable<Sandbox[]> {
    return this.http.get<Sandbox[]>(`${this.baseUrl}/sandboxes`);
  }

  getSandboxConnection(id: string): Observable<SandboxConnection> {
    return this.http.get<SandboxConnection>(`${this.baseUrl}/sandboxes/${id}/connection`);
  }

  destroySandbox(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/sandboxes/${id}`);
  }

  // ── MCP Configs ───────────────────────────────────────────────

  listMcpConfigs(): Observable<McpServerConfig[]> {
    return this.http.get<McpServerConfig[]>(`${this.baseUrl}/mcp`);
  }

  createMcpConfig(body: Record<string, unknown>): Observable<McpServerConfig> {
    return this.http.post<McpServerConfig>(`${this.baseUrl}/mcp`, body);
  }

  toggleMcpConfig(id: string): Observable<McpServerConfig> {
    return this.http.post<McpServerConfig>(`${this.baseUrl}/mcp/${id}/toggle`, {});
  }

  deleteMcpConfig(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/mcp/${id}`);
  }

  // ── Artifact Feeds ────────────────────────────────────────────

  listEnabledFeeds(): Observable<ArtifactFeedConfig[]> {
    return this.http.get<ArtifactFeedConfig[]>(`${this.baseUrl}/artifact/enabled`);
  }

  listAllFeeds(): Observable<ArtifactFeedConfig[]> {
    return this.http.get<ArtifactFeedConfig[]>(`${this.baseUrl}/artifact`);
  }

  createFeed(body: Record<string, unknown>): Observable<ArtifactFeedConfig> {
    return this.http.post<ArtifactFeedConfig>(`${this.baseUrl}/artifact`, body);
  }

  deleteFeed(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/artifact/${id}`);
  }

  // ── Linked Providers ──────────────────────────────────────────

  listLinkedProviders(): Observable<LinkedProvider[]> {
    return this.http.get<LinkedProvider[]>(`${this.baseUrl}/linked-providers`);
  }

  linkPat(provider: string, pat: string): Observable<LinkedProvider> {
    return this.http.post<LinkedProvider>(`${this.baseUrl}/linked-providers/pat`, { provider, pat });
  }

  unlinkProvider(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/linked-providers/${id}`);
  }

  // ── Users ─────────────────────────────────────────────────────

  suggestUsers(query: string): Observable<UserSuggestion[]> {
    return this.http.get<UserSuggestion[]>(`${this.baseUrl}/users/suggest`, { params: { q: query } });
  }
}
