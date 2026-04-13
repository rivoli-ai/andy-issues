// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { SettingsComponent } from './settings.component';

describe('SettingsComponent', () => {
  let component: SettingsComponent;
  let fixture: ComponentFixture<SettingsComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent, HttpClientTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushInitialRequests(): void {
    httpMock.expectOne('/api/linked-providers').flush([]);
    httpMock.expectOne('/api/mcp').flush([]);
    httpMock.expectOne('/api/artifact').flush([]);
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load all data on init', () => {
    fixture.detectChanges();

    httpMock.expectOne('/api/linked-providers').flush([
      { id: '1', provider: 'github', accountLogin: 'user', createdAt: '2026-01-01' }
    ]);
    httpMock.expectOne('/api/mcp').flush([]);
    httpMock.expectOne('/api/artifact').flush([]);

    fixture.detectChanges();
    expect(component.providers.length).toBe(1);
  });

  it('should link a PAT', () => {
    fixture.detectChanges();
    flushInitialRequests();

    component.patProvider = 'github';
    component.patValue = 'ghp_test123';
    component.linkPat();

    const req = httpMock.expectOne('/api/linked-providers/pat');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.provider).toBe('github');
    expect(req.request.body.pat).toBe('ghp_test123');
    req.flush({ id: '2', provider: 'github' });

    // Reload triggered
    httpMock.expectOne('/api/linked-providers').flush([]);
  });

  it('should switch tabs', () => {
    expect(component.activeTab).toBe('Source Control');
    component.activeTab = 'MCP';
    expect(component.activeTab).toBe('MCP');
  });

  it('should create an MCP config', () => {
    fixture.detectChanges();
    flushInitialRequests();

    component.newMcpName = 'test-server';
    component.newMcpCommand = '/bin/test';
    component.addMcp();

    const req = httpMock.expectOne('/api/mcp');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.name).toBe('test-server');
    req.flush({ id: 'm1', name: 'test-server', type: 'stdio', enabled: true });

    httpMock.expectOne('/api/mcp').flush([]);
  });
});
