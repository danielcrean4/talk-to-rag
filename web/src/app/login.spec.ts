import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { MockInstance } from 'vitest';
import { Auth } from './auth';
import { Login, defaultDisplayName } from './login';

const user = { id: 'u1', email: 'ada@example.com', displayName: 'Ada' };

describe('defaultDisplayName', () => {
  it('uses the part of the email before the @', () => {
    expect(defaultDisplayName(' ada@example.com ')).toBe('ada');
  });
});

describe('Login', () => {
  let http: HttpTestingController;
  let navigate: MockInstance<Router['navigate']>;

  function render() {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function button(element: HTMLElement, text: string): HTMLButtonElement {
    const found = Array.from(element.querySelectorAll('button')).find(
      (item) => item.textContent?.trim() === text,
    );
    if (!found) {
      throw new Error(`No button "${text}"`);
    }
    return found;
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  it('signs in with email and password', () => {
    const { fixture } = render();
    const login = fixture.componentInstance;
    login.email = 'ada@example.com';
    login.password = 'correct horse';
    login.submit();

    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ email: 'ada@example.com', password: 'correct horse' });
    request.flush({ accessToken: 't1', user });

    expect(TestBed.inject(Auth).token()).toBe('t1');
    expect(TestBed.inject(Auth).user()).toEqual(user);
    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('switches to registration, which asks for a display name', async () => {
    const { fixture, element } = render();
    expect(element.querySelector('input[name=displayName]')).toBeNull();

    button(element, 'Create one').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('h1')?.textContent).toContain('Create an account');
    expect(element.querySelector('input[name=displayName]')).not.toBeNull();
    expect(button(element, 'Create account')).toBeTruthy();
  });

  it('registers, defaulting the display name to the start of the email, and signs in', () => {
    const { fixture } = render();
    const login = fixture.componentInstance;
    login.toggleMode();
    login.email = 'ada@example.com';
    login.password = 'correct horse';
    login.submit();

    const request = http.expectOne('/api/auth/register');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'ada@example.com',
      displayName: 'ada',
      password: 'correct horse',
    });
    request.flush(
      { accessToken: 't2', user: { ...user, displayName: 'ada' } },
      { status: 201, statusText: 'Created' },
    );

    expect(TestBed.inject(Auth).token()).toBe('t2');
    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('shows why the API refused a registration', () => {
    const { fixture, element } = render();
    const login = fixture.componentInstance;
    login.toggleMode();
    login.email = 'ada@example.com';
    login.displayName = 'Ada';
    login.password = 'short';
    login.submit();

    const request = http.expectOne('/api/auth/register');
    expect(request.request.body.displayName).toBe('Ada');
    request.flush(
      { title: 'Bad request', detail: 'Use a password of at least 8 characters.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(element.querySelector('[role=alert]')?.textContent).toContain(
      'Use a password of at least 8 characters.',
    );
    expect(TestBed.inject(Auth).token()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });
});
