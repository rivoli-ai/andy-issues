// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { SandboxesComponent } from './sandboxes.component';

describe('SandboxesComponent', () => {
  let component: SandboxesComponent;
  let fixture: ComponentFixture<SandboxesComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SandboxesComponent, HttpClientTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(SandboxesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    component.ngOnDestroy();
    httpMock.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load sandboxes and repos on init', () => {
    fixture.detectChanges();

    const sandboxReq = httpMock.expectOne('/api/sandboxes/mine');
    expect(sandboxReq.request.method).toBe('GET');
    sandboxReq.flush({ items: [{ id: 's1', containerId: 'c1', repositoryId: 'r1', repositoryName: 'Repo', purpose: 'Interactive', branch: 'main', status: 'Running' }], capacity: { current: 1, max: 3, tenantMax: 20 } });

    const repoReq = httpMock.expectOne(r => r.url.includes('/api/repositories'));
    repoReq.flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });

    fixture.detectChanges();
    expect(component.sandboxes.length).toBe(1);
  });

  it('should fetch connection info on connect', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/sandboxes/mine').flush({ items: [], capacity: { current: 0, max: 3, tenantMax: 20 } });
    httpMock.expectOne(r => r.url.includes('/api/repositories')).flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });

    const sandbox = { id: 's1' } as any;
    component.connect(sandbox);

    const connReq = httpMock.expectOne('/api/sandboxes/s1/connection');
    connReq.flush({ ideEndpoint: 'https://ide.test', vncEndpoint: null, sshEndpoint: null });

    expect(component.connections['s1'].ideEndpoint).toBe('https://ide.test');
  });
  it('requires confirmation, prevents duplicate close requests and reports partial failures', () => {
    component.closeAll();
    httpMock.expectNone('/api/sandboxes/mine');
    component.showCloseAll = true;
    component.closeAll();
    component.closeAll();
    const close = httpMock.expectOne('/api/sandboxes/mine');
    expect(close.request.method).toBe('DELETE');
    expect(component.pending).toBeTrue();
    component.dismissCloseAll();
    expect(component.showCloseAll).toBeTrue();
    close.flush({ destroyed: ['one'], failed: [{ id: 'two', reason: 'Retry' }] });
    httpMock.expectOne('/api/sandboxes/mine').flush({ items: [], capacity: { current: 1, max: 3, tenantMax: 20 } });
    expect(component.closeFailures).toEqual([{ id: 'two', reason: 'Retry' }]);
    expect(component.pending).toBeFalse();
    expect(component.capacity.current).toBe(1);
  });

  it('keeps create input and refreshes capacity after a conflict', () => {
    component.createRepoId = 'repo';
    component.createBranch = 'work';
    component.showCreate = true;
    component.doCreate();
    component.doCreate();
    httpMock.expectOne('/api/sandboxes').flush({}, { status: 409, statusText: 'Conflict' });
    httpMock.expectOne('/api/sandboxes/mine').flush({ items: [], capacity: { current: 3, max: 3, tenantMax: 20 } });
    expect(component.createBranch).toBe('work');
    expect(component.showCreate).toBeTrue();
    expect(component.error).toContain('capacity');
  });

});
