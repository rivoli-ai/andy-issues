import { SandboxDockComponent } from './shared/ui/sandbox-dock.component';
import { ServiceShellComponent, ServiceNavItem } from './shared/ui/service-shell.component';
// Copyright (c) Rivoli AI 2026. All rights reserved.

import { Component, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { OidcSecurityService } from 'angular-auth-oidc-client';

@Component({
  selector: 'app-root',
  imports: [SandboxDockComponent, ServiceShellComponent, RouterOutlet, CommonModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent implements OnInit {
  readonly navigation: ServiceNavItem[] = [
  {
    "path": "/dashboard",
    "label": "Overview",
    "group": "Workspace"
  },
  {
    "path": "/repositories",
    "label": "Repositories",
    "group": "Plan & refine"
  },
  {
    "path": "/sandboxes",
    "label": "Sandboxes",
    "group": "Plan & refine"
  },
  {
    "path": "/settings",
    "label": "Settings",
    "group": "Resources"
  },
  {
    "path": "/help",
    "label": "Help",
    "group": "Resources"
  }
];

  title = 'Andy Issues';
  isAuthenticated = false;
  userName = '';

  constructor(private oidcService: OidcSecurityService) {}

  ngOnInit(): void {
    this.oidcService.checkAuth().subscribe(({ isAuthenticated, userData }) => {
      this.isAuthenticated = isAuthenticated;
      this.userName = userData?.name || userData?.email || '';
    });
  }

  login(): void {
    this.oidcService.authorize();
  }

  logout(): void {
    this.isAuthenticated = false;
    this.oidcService.logoff().subscribe();
  }
}
