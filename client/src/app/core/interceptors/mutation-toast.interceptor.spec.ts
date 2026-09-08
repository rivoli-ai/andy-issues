import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ToastService } from '@andy-ui/angular';
import { mutationToastInterceptor } from './mutation-toast.interceptor';

describe('Mutation toasts', () => {
  it('uses shared notifications for mutations while reads stay quiet', () => {
    const toast = jasmine.createSpyObj('ToastService', ['success', 'error']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([mutationToastInterceptor])), provideHttpClientTesting(), { provide: ToastService, useValue: toast }] });
    const client = TestBed.inject(HttpClient); const http = TestBed.inject(HttpTestingController);
    client.get('/api/repositories').subscribe(); http.expectOne('/api/repositories').flush([]);
    expect(toast.success).not.toHaveBeenCalled();
    client.post('/api/repositories', {}).subscribe(); http.expectOne('/api/repositories').flush({});
    expect(toast.success).toHaveBeenCalledWith('Changes saved.');
    client.post('/api/stories/id/refine', {}).subscribe(); http.expectOne('/api/stories/id/refine').flush({}, {status:202,statusText:'Accepted'});
    expect(toast.success).toHaveBeenCalledWith('Request accepted.');
    client.delete('/api/repositories/id').subscribe({ error: () => {} });
    http.expectOne('/api/repositories/id').flush({}, { status: 500, statusText: 'Failure' });
    expect(toast.error).toHaveBeenCalled(); http.verify();
  });
});
