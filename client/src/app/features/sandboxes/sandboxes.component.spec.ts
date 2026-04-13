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

    const sandboxReq = httpMock.expectOne('/api/sandboxes');
    expect(sandboxReq.request.method).toBe('GET');
    sandboxReq.flush([{ id: 's1', containerId: 'c1', repositoryId: 'r1', branch: 'main', status: 'Running' }]);

    const repoReq = httpMock.expectOne(r => r.url.includes('/api/repositories'));
    repoReq.flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });

    fixture.detectChanges();
    expect(component.sandboxes.length).toBe(1);
  });

  it('should fetch connection info on connect', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/sandboxes').flush([]);
    httpMock.expectOne(r => r.url.includes('/api/repositories')).flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });

    const sandbox = { id: 's1' } as any;
    component.connect(sandbox);

    const connReq = httpMock.expectOne('/api/sandboxes/s1/connection');
    connReq.flush({ ideEndpoint: 'https://ide.test', vncEndpoint: null, sshEndpoint: null });

    expect(component.connections['s1'].ideEndpoint).toBe('https://ide.test');
  });
});
