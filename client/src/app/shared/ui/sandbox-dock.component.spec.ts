import { ComponentFixture, TestBed, fakeAsync, tick, flushMicrotasks } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { SandboxDockComponent } from './sandbox-dock.component';
import { SandboxDockService } from '../services/sandbox-dock.service';

const result = { items: [
  { id: 'one', repositoryName: 'api', branch: 'main', status: 'Running', vncEndpoint: 'ws://localhost:6080/one', ideEndpoint: null },
  { id: 'two', repositoryName: 'web', branch: 'main', status: 'Starting', vncEndpoint: 'http://localhost:6081/vnc', ideEndpoint: null },
  { id: 'stopped', repositoryName: 'old', branch: 'main', status: 'Stopped', vncEndpoint: 'ws://localhost:6080/stopped', ideEndpoint: null },
], capacity: { current: 2, max: 3, tenantMax: 20 } };

describe('SandboxDockComponent', () => {
  let fixture: ComponentFixture<SandboxDockComponent>;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [SandboxDockComponent, HttpClientTestingModule] });
    fixture = TestBed.createComponent(SandboxDockComponent);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { fixture.destroy(); http.verify(); });

  it('polls only mine without overlapping requests and stops when destroyed', fakeAsync(() => {
    fixture.detectChanges(); tick(0);
    const pending = http.expectOne('/api/sandboxes/mine');
    tick(10000); http.expectNone('/api/sandboxes/mine');
    pending.flush(result);
    expect(fixture.componentInstance.viewers.map(viewer => viewer.id)).toEqual(['one', 'two']);
    expect(fixture.componentInstance.viewers[1].status).toBe('starting');
    fixture.destroy(); tick(20000);
    TestBed.inject(SandboxDockService).open('one');
    http.expectNone('/api/sandboxes/mine');
  }));

  it('keeps closed viewers dismissed across refresh and reopens on Connect without destroying sandboxes', fakeAsync(() => {
    fixture.detectChanges(); tick(0); http.expectOne('/api/sandboxes/mine').flush(result);
    fixture.componentInstance.close('one');
    tick(10000); http.expectOne('/api/sandboxes/mine').flush(result);
    expect(fixture.componentInstance.viewers.map(viewer => viewer.id)).toEqual(['two']);
    TestBed.inject(SandboxDockService).open('one');
    http.expectOne('/api/sandboxes/mine').flush(result);
    expect(fixture.componentInstance.viewers.length).toBe(2);
    fixture.componentInstance.closeAll();
    expect(fixture.componentInstance.viewers).toEqual([]);
    expect(http.match(request => request.method === 'DELETE')).toEqual([]);
    flushMicrotasks();
    fixture.destroy();
  }));

  it('removes viewer URLs on authorization loss and recovers on a later successful poll', fakeAsync(() => {
    fixture.detectChanges(); tick(0); http.expectOne('/api/sandboxes/mine').flush(result);
    tick(10000); http.expectOne('/api/sandboxes/mine').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(fixture.componentInstance.viewers).toEqual([]);
    tick(10000); http.expectOne('/api/sandboxes/mine').flush(result);
    expect(fixture.componentInstance.viewers.length).toBe(2);
    expect(fixture.componentInstance.error).toBe('');
    fixture.destroy();
  }));
});
