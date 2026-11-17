import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Auth, authInterceptor, signedInGuard } from './auth';
import { startSession } from './startup';

const user = { id: 'u1', email: 'ada@example.com', displayName: 'Ada' };

describe('startSession', () => {
  let http: HttpTestingController;
  let auth: Auth;

  function start() {
    TestBed.runInInjectionContext(startSession).subscribe();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(Auth);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  it('signs in the local user when the API runs without accounts', () => {
    start();
    http
      .expectOne('/api/auth/session')
      .flush({ singleUser: true, session: { accessToken: 't1', user } });

    expect(auth.singleUser).toBe(true);
    expect(auth.token()).toBe('t1');
    expect(auth.unreachable()).toBeNull();
  });

  it('checks a kept token when the API has accounts', () => {
    auth.signIn('kept');
    start();
    http.expectOne('/api/auth/session').flush({ singleUser: false, session: null });
    http.expectOne('/api/auth/me').flush(user);

    expect(auth.user()).toEqual(user);
    expect(auth.unreachable()).toBeNull();
  });

  it('shows sign-in when the API answers 404', () => {
    auth.signIn('kept');
    start();
    http
      .expectOne('/api/auth/session')
      .flush({ title: 'Not found' }, { status: 404, statusText: 'Not Found' });

    expect(auth.signedIn()).toBe(false);
    expect(auth.unreachable()).toBeNull();
  });

  it('forgets a kept token whose account no longer exists', () => {
    auth.signIn('kept');
    start();
    http.expectOne('/api/auth/session').flush({ singleUser: false, session: null });
    http
      .expectOne('/api/auth/me')
      .flush(
        { detail: 'This account no longer exists.' },
        { status: 404, statusText: 'Not Found' },
      );

    expect(auth.signedIn()).toBe(false);
    expect(auth.unreachable()).toBeNull();
  });

  it('reports an API that cannot be reached instead of offering a sign-in', () => {
    start();
    http.expectOne('/api/auth/session').error(new ProgressEvent('error'), { status: 0 });

    expect(auth.unreachable()?.reason).toContain('not reachable');
    expect(auth.unreachable()?.url).toBe(location.href);
    expect(TestBed.runInInjectionContext(() => signedInGuard({} as never, {} as never))).toBe(
      false,
    );
  });

  it('reports a server error the same way', () => {
    start();
    http
      .expectOne('/api/auth/session')
      .flush(
        { title: 'Service unavailable', detail: 'The database is not reachable.' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    expect(auth.unreachable()?.reason).toBe('The database is not reachable.');
  });
});
