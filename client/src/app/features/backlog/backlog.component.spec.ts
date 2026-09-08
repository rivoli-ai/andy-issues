// Copyright (c) Rivoli AI 2026. All rights reserved.

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { BacklogComponent } from './backlog.component';

describe('BacklogComponent', () => {
  let component: BacklogComponent;
  let fixture: ComponentFixture<BacklogComponent>;
  let httpMock: HttpTestingController;
  let params: BehaviorSubject<{repoId: string}>;

  beforeEach(async () => {
    params = new BehaviorSubject({repoId: 'test-repo-id'});
    await TestBed.configureTestingModule({
      imports: [BacklogComponent, HttpClientTestingModule, RouterTestingModule],
      providers: [
        { provide: ActivatedRoute, useValue: { params } },
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
    httpMock.expectOne('/api/repositories/test-repo-id').flush({id:'test-repo-id',name:'Release workspace'});
    httpMock.expectOne('/api/repositories/test-repo-id/agent-rules/profiles').flush([]);

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
    httpMock.expectOne('/api/repositories/test-repo-id').flush({id:'test-repo-id',name:'Release workspace'});
    httpMock.expectOne('/api/repositories/test-repo-id/agent-rules/profiles').flush([]);
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
  function initialize(): void {
    fixture.detectChanges();
    httpMock.expectOne('/api/repositories/test-repo-id').flush({id:'test-repo-id',name:'Release workspace'});
    httpMock.expectOne('/api/repositories/test-repo-id/agent-rules/profiles').flush([]);
    httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({repositoryId:'test-repo-id',epics:[]});
  }
  it('keeps failed input in the dialog and prevents duplicate create requests and dismissal while pending', () => {
    initialize(); component.openEpic(); component.newEpicTitle = 'Retained epic'; component.newEpicDesc = 'Retained details';
    component.addEpic(); component.addEpic(); component.closeCreate();
    expect(component.showAddEpic).toBeTrue(); expect(component.pendingCreate).toBe('epic');
    httpMock.expectOne('/api/repositories/test-repo-id/epics').flush({}, {status:503,statusText:'Unavailable'});
    fixture.detectChanges();
    expect(component.pendingCreate).toBeNull(); expect(component.newEpicTitle).toBe('Retained epic'); expect(component.newEpicDesc).toBe('Retained details');
    expect(fixture.nativeElement.querySelector('[role="dialog"] [role="alert"]').textContent).toContain('Failed to create epic');
    expect(fixture.nativeElement.querySelector('label[for="newEpicTitle"]').textContent).toBe('Epic title');
  });
  it('submits a feature against the selected parent and keeps failures local to that dialog', () => {
    initialize(); component.openFeature('epic-parent'); component.newFeatureTitle = 'Feature text'; component.addFeature(); component.addFeature();
    const req=httpMock.expectOne('/api/epics/epic-parent/features'); expect(req.request.body.title).toBe('Feature text');
    req.flush({}, {status:500,statusText:'Failure'}); expect(component.addFeatureEpicId).toBe('epic-parent'); expect(component.newFeatureTitle).toBe('Feature text'); expect(component.createError).toContain('Failed to create feature');
  });
  it('retains story fields and profile after a rejected create', () => {
    initialize(); component.openStory('feature-parent'); component.newStoryTitle='Story text'; component.newStoryDesc='Details'; component.newStoryPoints=3; component.newStoryRuleId='rule-a';
    component.addStory(); component.addStory(); const req=httpMock.expectOne('/api/features/feature-parent/stories');
    expect(req.request.body.agentRuleId).toBe('rule-a'); req.flush({}, {status:500,statusText:'Failure'});
    expect(component.addStoryFeatureId).toBe('feature-parent'); expect(component.newStoryTitle).toBe('Story text'); expect(component.newStoryPoints).toBe(3); expect(component.newStoryRuleId).toBe('rule-a');
  });
  it('shows loading and a retryable load failure', () => {
    initialize(); component.load(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Loading backlog');
    httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({}, {status:503,statusText:'Unavailable'}); fixture.detectChanges();
    expect(component.loading).toBeFalse(); expect(component.loadFailed).toBeTrue(); expect(fixture.nativeElement.textContent).toContain('Retry');
    component.load(); httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({repositoryId:'test-repo-id',epics:[]}); expect(component.loadFailed).toBeFalse();
  });
  it('keeps the persisted story status when an update fails and blocks duplicate writes', () => {
    initialize(); const story={id:'s1',status:'Draft'} as any;
    component.setStatus(story,'Ready'); component.setStatus(story,'Done');
    httpMock.expectOne('/api/stories/s1/status').flush({}, {status:409,statusText:'Conflict'});
    expect(story.status).toBe('Draft'); expect(component.pendingStatuses.size).toBe(0); expect(component.storyErrors.get('s1')).toContain('Failed to update status');
  });
  it('starts refinement in the existing repository and recognizes the server discriminator', () => {
    initialize(); const story={id:'s1',status:'Draft',triageState:{kind:'NotTriaged'}} as any;
    component.refine(story); component.refine(story);
    httpMock.expectOne('/api/stories/s1/refine').flush({refineRunId:'run1',refineVersion:1},{status:202,statusText:'Accepted'});
    expect(component.repositoryId).toBe('test-repo-id'); expect(story.triageState.kind).toBe('Triaging');
    component.refine(story); httpMock.expectNone('/api/stories/s1/refine');
  });
  it('does not let an earlier load overwrite the latest refresh', () => {
    initialize(); component.load(); const earlier=httpMock.expectOne('/api/repositories/test-repo-id/backlog'); component.load();
    httpMock.expectOne('/api/repositories/test-repo-id/backlog').flush({repositoryId:'latest',epics:[]}); earlier.flush({repositoryId:'stale',epics:[]});
    expect(component.backlog?.repositoryId).toBe('latest');
  });

  it('cancels old repository requests and clears its unsaved form when the route changes', () => {
    initialize(); component.openEpic(); component.newEpicTitle='Old repository text'; component.addEpic();
    const previous=httpMock.expectOne('/api/repositories/test-repo-id/epics');
    params.next({repoId:'next-repo'});
    expect(previous.cancelled).toBeTrue(); expect(component.newEpicTitle).toBe(''); expect(component.showAddEpic).toBeFalse(); expect(component.pendingCreate).toBeNull();
    httpMock.expectOne('/api/repositories/next-repo').flush({id:'next-repo',name:'Next repo'});
    httpMock.expectOne('/api/repositories/next-repo/agent-rules/profiles').flush([]);
    httpMock.expectOne('/api/repositories/next-repo/backlog').flush({repositoryId:'next-repo',epics:[]});
    expect(component.repositoryName).toBe('Next repo');
  });
});
