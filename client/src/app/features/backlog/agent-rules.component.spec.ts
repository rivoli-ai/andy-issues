import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AgentRulesComponent } from './agent-rules.component';

describe('AgentRulesComponent', () => {
  let fixture: ComponentFixture<AgentRulesComponent>;
  let component: AgentRulesComponent;
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AgentRulesComponent, HttpClientTestingModule] }).compileComponents();
    fixture = TestBed.createComponent(AgentRulesComponent); component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('repositoryId', 'repo'); fixture.detectChanges();
    http.expectOne('/api/repositories/repo/agent-rules').flush({ rules: '', profiles: [], canEdit: true });
  });
  afterEach(() => http.verify());
  it('saves reordering and promotes the last profile after deleting the default', () => {
    component.add(); component.add(); component.add();
    component.profiles[0].name = 'First'; component.profiles[1].name = 'Second'; component.profiles[2].name = 'Third';
    component.move(2, -1); component.remove(0);
    expect(component.profiles.find(p => p.isDefault)?.name).toBe('Second');
    component.save();
    const request = http.expectOne('/api/repositories/repo/agent-rules/replace');
    expect(request.request.body.map((p: {name: string}) => p.name)).toEqual(['Third', 'Second']);
    expect(request.request.body.map((p: {sortOrder: number}) => p.sortOrder)).toEqual([0, 1]);
    request.flush(request.request.body);
    expect(component.notice).toBe('Profiles saved.');
  });
  it('rejects duplicate names and preserves edits on a failed save', () => {
    component.add(); component.add(); component.profiles[1].name = component.profiles[0].name.toUpperCase();
    expect(component.valid).toBeFalse(); component.save(); http.expectNone('/api/repositories/repo/agent-rules/replace');
    component.profiles[1].name = 'Review'; component.save();
    http.expectOne('/api/repositories/repo/agent-rules/replace').flush({ error: 'conflict' }, { status: 409, statusText: 'Conflict' });
    expect(component.profiles[1].name).toBe('Review'); expect(component.error).toBe('conflict');
  });
  it('sanitizes HTML in the shared Markdown preview', async () => {
    component.add(); component.profiles[0].body = '**Safe**<img src="x" onerror="alert(1)"><script>alert(1)</script>';
    fixture.detectChanges();
    const element = fixture.nativeElement.querySelector('.preview andy-markdown') as HTMLElement & { updateComplete: Promise<boolean> };
    await element.updateComplete;
    const preview = element.shadowRoot!;
    expect(preview.querySelector('strong')?.textContent).toBe('Safe');
    expect(preview.querySelector('script')).toBeNull();
    expect(preview.querySelector('img')?.getAttribute('onerror')).toBeNull();
  });
  it('does not submit edits for a shared reader', () => {
    component.canEdit = false; component.add(); component.save();
    http.expectNone('/api/repositories/repo/agent-rules/replace');
  });
});
