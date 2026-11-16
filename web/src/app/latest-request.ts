import { DestroyRef, inject } from '@angular/core';
import { Observable, Observer, Subscription } from 'rxjs';

/**
 * Keeps one request running at a time. Starting another, calling `cancel()` or destroying the
 * owner unsubscribes from the one before, so its response never lands after a newer one.
 */
export class LatestRequest {
  private current?: Subscription;

  constructor(destroyRef: DestroyRef) {
    destroyRef.onDestroy(() => this.cancel());
  }

  run<T>(request: Observable<T>, observer: Partial<Observer<T>>): void {
    this.cancel();
    this.current = request.subscribe(observer);
  }

  cancel(): void {
    this.current?.unsubscribe();
    this.current = undefined;
  }
}

/** A `LatestRequest` tied to the current component or service; call it in a field initializer. */
export function latestRequest(): LatestRequest {
  return new LatestRequest(inject(DestroyRef));
}
