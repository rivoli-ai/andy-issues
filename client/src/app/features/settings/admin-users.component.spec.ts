import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AdminUsersComponent } from './admin-users.component';
import { SettingsComponent } from './settings.component';
import { ApiService } from '../../shared/services/api.service';
import { of } from 'rxjs';

describe('Admin users', () => {
  it('hides the Users tab without permission', () => {
    const api = { adminUserAccess: () => of({ canRead: false, manageUrl: null }),
      listLinkedProviders: () => of([]), listMcpConfigs: () => of([]), listAllFeeds: () => of([]) } as unknown as ApiService;
    const component = new SettingsComponent(api);
    component.ngOnInit();
    expect(component.tabs).not.toContain('Users');
  });

  it('debounces search and cancels obsolete requests', fakeAsync(() => {
    TestBed.configureTestingModule({ imports: [AdminUsersComponent, HttpClientTestingModule] });
    const fixture = TestBed.createComponent(AdminUsersComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    tick(300);
    const initial = http.expectOne(r => r.url === '/api/admin/users');
    fixture.componentInstance.query = 'alice';
    fixture.componentInstance.search();
    tick(150);
    fixture.componentInstance.query = 'alice@example';
    fixture.componentInstance.search();
    tick(299);
    http.expectNone(r => r.params.get('query') === 'alice');
    tick(1);
    expect(initial.cancelled).toBeTrue();
    const request = http.expectOne(r => r.params.get('query') === 'alice@example');
    request.flush({ items: [], total: 0 });
    expect(fixture.componentInstance.loading).toBeFalse();
    fixture.destroy();
    http.verify();
  }));
});
