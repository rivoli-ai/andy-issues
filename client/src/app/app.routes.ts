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
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'repositories',
    loadComponent: () =>
      import('./features/repositories/repositories.component').then(
        (m) => m.RepositoriesComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'backlog/:repoId',
    loadComponent: () =>
      import('./features/backlog/backlog.component').then(
        (m) => m.BacklogComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'sandboxes',
    loadComponent: () =>
      import('./features/sandboxes/sandboxes.component').then(
        (m) => m.SandboxesComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'settings',
    loadComponent: () =>
      import('./features/settings/settings.component').then(
        (m) => m.SettingsComponent
      ),
    canActivate: [authGuard],
  },
  {
    path: 'help',
    loadComponent: () =>
      import('./features/help/help.component').then(
        (m) => m.HelpComponent
      ),
  },
  {
    path: 'callback',
    loadComponent: () =>
      import('./core/auth/callback.component').then(
        (m) => m.CallbackComponent
      ),
  },
];
