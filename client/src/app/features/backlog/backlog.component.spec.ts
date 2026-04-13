// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { BacklogComponent } from './backlog.component';

describe('BacklogComponent', () => {
  let component: BacklogComponent;
  let fixture: ComponentFixture<BacklogComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BacklogComponent, HttpClientTestingModule, RouterTestingModule],
      providers: [
        { provide: ActivatedRoute, useValue: { params: of({ repoId: 'test-repo-id' }) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BacklogComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load backlog on init with repo id from route', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne('/api/repositories/test-repo-id/backlog');
    expect(req.request.method).toBe('GET');

    req.flush({
      repositoryId: 'test-repo-id',
      epics: [{
        id: 'e1', repositoryId: 'test-repo-id', title: 'Epic 1', order: 0, features: [{
          id: 'f1', epicId: 'e1', title: 'Feature 1', order: 0, stories: [{
            id: 's1', featureId: 'f1', title: 'Story 1', status: 'Draft', order: 0, storyPoints: 3
          }]
        }]
      }]
    });
    fixture.detectChanges();

    expect(component.backlog).toBeTruthy();
    expect(component.backlog!.epics.length).toBe(1);
    expect(component.backlog!.epics[0].features[0].stories[0].title).toBe('Story 1');
  });

  it('should create an epic via API', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({ repositoryId: 'test-repo-id', epics: [] });

    component.newEpicTitle = 'New Epic';
    component.addEpic();

    const createReq = httpMock.expectOne('/api/repositories/test-repo-id/epics');
    expect(createReq.request.method).toBe('POST');
    expect(createReq.request.body.title).toBe('New Epic');
    createReq.flush({ id: 'e2', title: 'New Epic', repositoryId: 'test-repo-id', order: 0, features: [] });

    // Reload triggered
    httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({ repositoryId: 'test-repo-id', epics: [] });
  });
});
