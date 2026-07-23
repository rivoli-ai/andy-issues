// Copyright (c) Rivoli AI 2026. All rights reserved.
// Karma test entry point — required by angular.json and tsconfig.spec.json.

import 'zone.js/testing';
import { getTestBed } from '@angular/core/testing';
import {
  BrowserTestingModule,
  platformBrowserTesting,
} from '@angular/platform-browser/testing';

getTestBed().initTestEnvironment(
  BrowserTestingModule,
  platformBrowserTesting(),
);
