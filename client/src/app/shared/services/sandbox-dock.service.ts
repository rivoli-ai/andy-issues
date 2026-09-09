import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SandboxDockService {
  private readonly requests = new Subject<string>();
  readonly openRequests$ = this.requests.asObservable();
  open(id: string): void { this.requests.next(id); }
}
