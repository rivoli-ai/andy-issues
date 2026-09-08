import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ServiceShellComponent } from './service-shell.component';
import { ThemeService } from './theme.service';

@Component({ template: '' })
class Page {}

describe('Andy UI service shell', () => {
  beforeEach(async () => {
    localStorage.removeItem(ThemeService.storageKey);
    await TestBed.configureTestingModule({ imports: [ServiceShellComponent], providers: [provideRouter([
      { path: 'dashboard', component: Page, data: { breadcrumb: 'Overview' } },
      { path: 'backlog/:repoId', component: Page, data: { breadcrumb: 'Backlog' } },
    ])] }).compileComponents();
  });
  afterEach(() => { localStorage.removeItem(ThemeService.storageKey); localStorage.removeItem('andy-ui-theme'); });
  it('renders shared shell/sidebar/breadcrumb and updates router-derived labels', async () => {
    const fixture = TestBed.createComponent(ServiceShellComponent);
    fixture.componentRef.setInput('serviceName', 'Issues');
    fixture.componentRef.setInput('links', [{ path: '/dashboard', label: 'Overview', group: 'Workspace' }]);
    await TestBed.inject(Router).navigateByUrl('/backlog/repo');
    fixture.detectChanges(); await fixture.whenStable();
    const shell: HTMLElement = fixture.nativeElement;
    expect(shell.querySelector('andy-app-shell')).not.toBeNull();
    expect(shell.querySelector('andy-sidebar')).not.toBeNull();
    expect(shell.querySelector('andy-breadcrumb')?.textContent).toContain('Repositories');
    expect(shell.querySelector('andy-breadcrumb')?.textContent).toContain('Backlog');
    expect(shell.querySelector('andy-nav-item')?.textContent).toContain('Overview');
  });
  it('persists a shared theme toggle to the application preference', async () => {
    const fixture = TestBed.createComponent(ServiceShellComponent); fixture.detectChanges(); await fixture.whenStable();
    const toggle: HTMLElement = fixture.nativeElement.querySelector('andy-theme-toggle');
    toggle.dispatchEvent(new CustomEvent('andy-theme-change', { detail: 'dark', bubbles: true }));
    expect(localStorage.getItem(ThemeService.storageKey)).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });
  it('closes the mobile drawer with Escape and restores its trigger', async () => {
    const fixture = TestBed.createComponent(ServiceShellComponent); fixture.componentInstance.mobile = true;
    fixture.detectChanges(); await fixture.whenStable();
    fixture.componentInstance.openMenu(); fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.componentInstance.menuOpen).toBeTrue();
    fixture.componentInstance.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(fixture.componentInstance.menuOpen).toBeFalse();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.menu-button'));
  });
  it('uses a stored theme before falling back to the system preference', () => {
    localStorage.setItem(ThemeService.storageKey, 'dark');
    TestBed.inject(ThemeService);
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });
});
