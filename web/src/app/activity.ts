import { Injectable, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { errorMessage } from './auth';

/**
 * The action a page is running, shared with its child components: its label while it runs
 * and the error the last one ended with. Provide it on the page component.
 */
@Injectable()
export class Activity {
  readonly busy = signal('');
  readonly error = signal('');

  run<T>(label: string, request: Observable<T>, done: (result: T) => void): void {
    this.busy.set(label);
    this.error.set('');
    request.subscribe({
      next: (result) => {
        this.busy.set('');
        done(result);
      },
      error: (error) => {
        this.busy.set('');
        this.error.set(errorMessage(error));
      },
    });
  }

  /** Shows the error of a request made outside `run`, such as a load. */
  fail(error: unknown): void {
    this.error.set(errorMessage(error));
  }
}
