import { inject } from '@angular/core';
import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { ToastService } from '@andy-ui/angular';
import { tap } from 'rxjs';

export const mutationToastInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);
  const mutation = /^(POST|PUT|PATCH|DELETE)$/.test(request.method) && request.url.startsWith('/api/');
  return next(request).pipe(tap({
    next: event => {
      if (mutation && event instanceof HttpResponse) toast.success(request.method === 'DELETE' ? 'Removed successfully.' : 'Changes saved.');
    },
    error: () => { if (mutation) toast.error('The change could not be saved. Please try again.'); },
  }));
};
