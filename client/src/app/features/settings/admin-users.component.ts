import { Component, Input, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, catchError, debounceTime, of, startWith, switchMap, takeUntil } from 'rxjs';
import { AdminUsers, ApiService } from '../../shared/services/api.service';

@Component({
  selector: 'app-admin-users',
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Users</h2>
    <p>Role membership is provided by Andy RBAC. This view is read-only.</p>
    <a *ngIf="manageUrl" [href]="manageUrl" target="_blank" rel="noopener noreferrer">Manage in Andy RBAC</a>
    <div class="filters">
      <label>Search users <input [(ngModel)]="query" (ngModelChange)="search()" placeholder="Email or display name" /></label>
      <label>Role <select [(ngModel)]="role" (ngModelChange)="search()">
        <option value="">All roles</option><option value="admin">Admin</option><option value="user">User</option><option value="viewer">Viewer</option>
      </select></label>
    </div>
    <p role="alert" *ngIf="error">{{ error }} <button (click)="refresh()">Retry</button></p>
    <p aria-live="polite">{{ loading ? 'Loading users…' : result.total + ' users' }}</p>
    <table *ngIf="result.items.length">
      <thead><tr><th>Email</th><th>Name</th><th>Roles</th><th>Last seen</th></tr></thead>
      <tbody><tr *ngFor="let user of result.items"><td>{{ user.email || '—' }}</td><td>{{ user.displayName || '—' }}</td>
        <td><span *ngFor="let name of user.roles" class="badge">{{ name }}</span></td><td>{{ user.lastSeenAt ? (user.lastSeenAt | date:'short') : '—' }}</td></tr></tbody>
    </table>
    <div class="filters"><button (click)="page(-1)" [disabled]="loading || skip === 0">Previous</button>
      <button (click)="page(1)" [disabled]="loading || skip + 50 >= result.total">Next</button></div>
  `,
  styles: [`
    .filters { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
    label { display: grid; gap: 6px; } input, select { padding: 8px; }
    table { width: 100%; text-align: left; border-collapse: collapse; } th, td { padding: 10px; border-bottom: 1px solid var(--border); }
    .badge { display: inline-block; padding: 2px 6px; margin-right: 4px; background: var(--background); border-radius: 4px; }
  `],
})
export class AdminUsersComponent implements OnInit, OnDestroy {
  @Input() manageUrl: string | null = null;
  query = ''; role = ''; skip = 0;
  result: AdminUsers = { items: [], total: 0 };
  loading = false; error = '';
  private readonly changes$ = new Subject<void>();
  private readonly destroyed$ = new Subject<void>();
  constructor(private api: ApiService) {}
  ngOnInit(): void {
    this.changes$.pipe(startWith(undefined), debounceTime(300), switchMap(() => {
      this.loading = true; this.error = '';
      return this.api.listAdminUsers(this.query, this.role, this.skip).pipe(catchError(() => {
        this.error = 'Could not load users from Andy RBAC.';
        return of({ items: [], total: 0 });
      }));
    }), takeUntil(this.destroyed$)).subscribe(result => { this.result = result; this.loading = false; });
  }
  search(): void { this.skip = 0; this.refresh(); }
  refresh(): void { this.changes$.next(); }
  page(direction: number): void { this.skip = Math.max(0, this.skip + direction * 50); this.refresh(); }
  ngOnDestroy(): void { this.destroyed$.next(); this.destroyed$.complete(); }
}
