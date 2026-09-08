import { ChangeDetectorRef, Component, CUSTOM_ELEMENTS_SCHEMA, DestroyRef, ElementRef, HostListener, Input, OnChanges, ViewChild, inject } from '@angular/core';
import { ThemeService } from './theme.service';
import { type AndyCrumb } from '@andy-ui/angular';
import { CommonModule, Location } from '@angular/common';
import { NavigationEnd, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';

export interface ServiceNavItem {
  path: string;
  label: string;
  group: string;
  visible?: boolean;
}

@Component({
  selector: 'app-service-shell',
  standalone: true,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  imports: [CommonModule],
  templateUrl: './service-shell.component.html',
  styleUrls: ['./service-shell.component.css'],
})
export class ServiceShellComponent implements OnChanges {
  breadcrumbs: AndyCrumb[] = [];
  @Input() chrome = true;
  @Input() serviceName = '';
  @Input() serviceDescription = '';
  @Input() links: ServiceNavItem[] = [];
  @ViewChild('drawer') drawer?: ElementRef<HTMLElement>;
  @ViewChild('menuButton') menuButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('mainContent') mainContent?: ElementRef<HTMLElement>;
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  readonly theme = inject(ThemeService);
  collapsed = false;
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  mobile = typeof window !== 'undefined' && window.innerWidth < 960;
  menuOpen = false;
  currentPath = this.router.url.split(/[?#]/)[0];

  constructor() {
    this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef))
      .subscribe(event => {
        this.currentPath = event.urlAfterRedirects.split(/[?#]/)[0];
        this.updateBreadcrumbs();
        this.closeMenu(false);
        requestAnimationFrame(() => this.mainContent?.nativeElement.focus({ preventScroll: true }));
      });
  }

  get visibleLinks(): ServiceNavItem[] { return this.links.filter(link => link.visible !== false); }
  get groups(): string[] { return [...new Set(this.visibleLinks.map(link => link.group))]; }
  trackLink(_index: number, link: ServiceNavItem): string { return link.path; }
  linksIn(group: string): ServiceNavItem[] { return this.visibleLinks.filter(link => link.group === group); }
  get homePath(): string { return this.visibleLinks[0]?.path ?? '/'; }
  isCurrent(link: ServiceNavItem): boolean { return this.currentLink?.path === link.path; }
  get currentLabel(): string {
    let route = this.router.routerState.snapshot.root;
    while (route.firstChild) route = route.firstChild;
    return route.data['breadcrumb'] ?? this.currentLink?.label ?? this.serviceName;
  }
  ngOnChanges(): void { this.updateBreadcrumbs(); }
  private updateBreadcrumbs(): void {
    const items: AndyCrumb[] = [{ label: this.serviceName, href: this.href(this.homePath) }];
    if (this.currentPath.startsWith('/backlog/')) items.push({ label: 'Repositories', href: this.href('/repositories') });
    items.push({ label: this.currentLabel });
    this.breadcrumbs = items;
  }
  href(path: string): string { return this.location.prepareExternalUrl(path); }
  navigateLink(event: MouseEvent): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    const anchor = (event.target as Element).closest<HTMLAnchorElement>('a[href]');
    if (!anchor || anchor.origin !== window.location.origin) return;
    event.preventDefault();
    void this.router.navigateByUrl(this.location.normalize(anchor.pathname + anchor.search + anchor.hash));
  }
  themeChanged(event: Event): void {
    const value = (event as CustomEvent).detail;
    if (value === 'light' || value === 'dark') this.theme.select(value);
  }
  collapseChanged(event: Event): void { this.collapsed = (event as CustomEvent<boolean>).detail; }
  get currentLink(): ServiceNavItem | undefined {
    return [...this.visibleLinks].sort((a, b) => b.path.length - a.path.length)
      .find(link => this.currentPath === link.path || this.currentPath.startsWith(link.path + '/'));
  }

  openMenu(): void {
    this.menuOpen = true;
    this.changeDetector.detectChanges();
    this.drawer?.nativeElement.querySelector<HTMLButtonElement>('button')?.focus();
  }
  closeMenu(restoreFocus = true): void {
    const wasOpen = this.menuOpen;
    this.menuOpen = false;
    if (wasOpen) this.changeDetector.detectChanges();
    if (wasOpen && restoreFocus) this.menuButton?.nativeElement.focus();
  }
  @HostListener('window:resize') onResize(): void {
    this.mobile = window.innerWidth < 960;
    if (!this.mobile) this.closeMenu(false);
  }
  @HostListener('document:keydown', ['$event']) onKeydown(event: KeyboardEvent): void {
    if (!this.mobile || !this.menuOpen) return;
    if (event.key === 'Escape') { event.preventDefault(); this.closeMenu(); return; }
    if (event.key !== 'Tab') return;
    const focusable = Array.from(this.drawer?.nativeElement.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex="0"]') ?? [])
      .filter(element => element.getClientRects().length > 0);
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (!first) return;
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
}
