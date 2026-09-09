import { Component, CUSTOM_ELEMENTS_SCHEMA, DestroyRef, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, exhaustMap, merge, of, Subject, timer } from 'rxjs';
import { type DockSandboxViewer } from '@andy-ui/angular';
import { ApiService, SandboxSummary } from '../services/api.service';
import { SandboxDockService } from '../services/sandbox-dock.service';

@Component({
  selector: 'app-sandbox-dock',
  imports: [CommonModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: `
    <p *ngIf="error" role="status">{{ error }}</p>
    <button *ngIf="all.length && !viewers.length" class="btn-secondary" (click)="restoreAll()">Show sandbox viewers ({{ all.length }})</button>
    <andy-dock-panel #dock minimized [viewers]="viewers" [maxVisible]="3"
      (andy-close)="close($any($event).detail)" (andy-close-all)="closeAll()"></andy-dock-panel>
  `,
})
export class SandboxDockComponent implements OnInit {
  @ViewChild('dock') dock?: ElementRef<HTMLElement & { minimized: boolean }>;
  readonly api = inject(ApiService);
  private readonly requests = inject(SandboxDockService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  private readonly closed = new Set<string>();
  all: DockSandboxViewer[] = [];
  viewers: DockSandboxViewer[] = [];
  error = '';

  ngOnInit(): void {
    this.requests.openRequests$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(id => {
      this.closed.delete(id);
      this.applyVisibility();
      if (this.dock) this.dock.nativeElement.minimized = false;
      this.refresh.next();
    });
    merge(timer(0, 10000), this.refresh).pipe(
      exhaustMap(() => this.api.listMySandboxes().pipe(catchError(error => {
        this.error = 'Sandbox viewers are unavailable. Retrying shortly.';
        if (error.status === 401 || error.status === 403) { this.all = []; this.applyVisibility(); }
        return of(null);
      }))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(result => {
      if (!result) return;
      this.error = '';
      this.all = result.items.filter(item => item.vncEndpoint && !['stopped', 'destroyed'].includes(item.status.toLowerCase()))
        .map(item => this.viewer(item));
      for (const id of this.closed) if (!this.all.some(viewer => viewer.id === id)) this.closed.delete(id);
      this.applyVisibility();
    });
  }

  private viewer(item: SandboxSummary): DockSandboxViewer {
    const status = item.status.toLowerCase();
    return {
      id: item.id,
      title: `${item.repositoryName} · ${item.branch}`,
      vncUrl: item.vncEndpoint!,
      ideUrl: item.ideEndpoint || undefined,
      status: status === 'running' ? 'running' : ['stopping', 'destroying'].includes(status) ? 'stopping' : ['error', 'failed'].includes(status) ? 'error' : 'starting',
    };
  }
  private applyVisibility(): void { this.viewers = this.all.filter(viewer => !this.closed.has(viewer.id)); }
  close(id: string): void { this.closed.add(id); this.applyVisibility(); }
  closeAll(): void { for (const viewer of this.all) this.closed.add(viewer.id); this.applyVisibility(); }
  restoreAll(): void {
    this.closed.clear(); this.applyVisibility();
    if (this.dock) this.dock.nativeElement.minimized = false;
  }
}
