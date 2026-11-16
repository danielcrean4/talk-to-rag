import { DestroyRef } from '@angular/core';
import { Subject } from 'rxjs';
import { LatestRequest } from './latest-request';

class FakeDestroyRef implements DestroyRef {
  private readonly callbacks: (() => void)[] = [];
  readonly destroyed = false;
  onDestroy(callback: () => void): () => void {
    this.callbacks.push(callback);
    return () => undefined;
  }
  destroy(): void {
    this.callbacks.forEach((callback) => callback());
  }
}

describe('LatestRequest', () => {
  it('drops the response of a request that a newer one replaced', () => {
    const latest = new LatestRequest(new FakeDestroyRef());
    const first = new Subject<string>();
    const second = new Subject<string>();
    const seen: string[] = [];

    latest.run(first, { next: (value) => seen.push(value) });
    latest.run(second, { next: (value) => seen.push(value) });
    second.next('second');
    first.next('first');

    expect(seen).toEqual(['second']);
  });

  it('drops the response once cancelled or once its owner is destroyed', () => {
    const owner = new FakeDestroyRef();
    const latest = new LatestRequest(owner);
    const request = new Subject<string>();
    const seen: string[] = [];

    latest.run(request, { next: (value) => seen.push(value) });
    latest.cancel();
    request.next('after cancel');
    latest.run(request, { next: (value) => seen.push(value) });
    owner.destroy();
    request.next('after destroy');

    expect(seen).toEqual([]);
  });
});
