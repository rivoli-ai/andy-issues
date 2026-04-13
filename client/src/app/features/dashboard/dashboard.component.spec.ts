// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent, HttpClientTestingModule, RouterTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load repo count and sandbox count on init', () => {
    fixture.detectChanges();

    const repoReq = httpMock.expectOne(r => r.url.includes('/api/repositories'));
    repoReq.flush({ items: [], page: 1, pageSize: 1, totalCount: 5 });

    const sandboxReq = httpMock.expectOne('/api/sandboxes');
    sandboxReq.flush([
      { id: '1', status: 'Running' },
      { id: '2', status: 'Stopped' },
    ]);

    fixture.detectChanges();
    expect(component.repoCount).toBe(5);
    expect(component.sandboxCount).toBe(2);
    expect(component.runningSandboxes).toBe(1);
  });
});
