// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'dashboard',
    data: { breadcrumb: 'Overview' },
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'repositories',
    data: { breadcrumb: 'Repositories' },
    loadComponent: () =>
      import('./features/repositories/repositories.component').then(
        (m) => m.RepositoriesComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'backlog/:repoId',
    data: { breadcrumb: 'Backlog' },
    loadComponent: () =>
      import('./features/backlog/backlog.component').then(
        (m) => m.BacklogComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'sandboxes',
    data: { breadcrumb: 'Sandboxes' },
    loadComponent: () =>
      import('./features/sandboxes/sandboxes.component').then(
        (m) => m.SandboxesComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'settings',
    data: { breadcrumb: 'Settings' },
    loadComponent: () =>
      import('./features/settings/settings.component').then(
        (m) => m.SettingsComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'help',
    data: { breadcrumb: 'Help' },
    loadComponent: () =>
      import('./features/help/help.component').then(
        (m) => m.HelpComponent
      ),
  },
  {
    path: 'callback',
    data: { breadcrumb: 'Sign in' },
    loadComponent: () =>
      import('./core/auth/callback.component').then(
        (m) => m.CallbackComponent
      ),
  },
];
