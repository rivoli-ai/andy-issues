import { Injectable, inject } from '@angular/core';
import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { ToastService } from '@andy-ui/angular';
import { tap } from 'rxjs';

// Keep only the latest transient notification; contextual errors remain in forms.
@Injectable({ providedIn: 'root' })
class MutationNotifications {
  private readonly toast = inject(ToastService);
  private dismiss?: () => void;
  success(message: string): void { this.dismiss?.(); this.dismiss = this.toast.success(message); }
  error(message: string): void { this.dismiss?.(); this.dismiss = this.toast.error(message); }
}

export const mutationToastInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(MutationNotifications);
  const mutation = /^(POST|PUT|PATCH|DELETE)$/.test(request.method) && request.url.startsWith('/api/');
  return next(request).pipe(tap({
    next: event => {
      if (mutation && event instanceof HttpResponse) toast.success(event.status === 202 ? 'Request accepted.' : request.method === 'DELETE' ? 'Removed successfully.' : 'Changes saved.');
    },
    error: () => { if (mutation) toast.error('The change could not be saved. Please try again.'); },
  }));
};
