import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, AuthResponse } from './api';
import { Auth, errorMessage } from './auth';

/** The name to register with when none is given: the part of the email before the @. */
export function defaultDisplayName(email: string): string {
  return email.trim().split('@')[0];
}

/** Sign in, or create an account, when the API runs with accounts. */
@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <form class="login" (ngSubmit)="submit()">
      <h1>{{ registering() ? 'Create an account' : 'Sign in' }}</h1>
      <label
        >Email
        <input type="email" name="email" [(ngModel)]="email" required autocomplete="username"
      /></label>
      @if (registering()) {
        <label
          >Display name
          <input
            name="displayName"
            [(ngModel)]="displayName"
            [placeholder]="defaultDisplayName(email)"
            autocomplete="nickname"
        /></label>
      }
      <label
        >Password
        <input
          type="password"
          name="password"
          [(ngModel)]="password"
          required
          [autocomplete]="registering() ? 'new-password' : 'current-password'"
      /></label>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      <button class="primary" type="submit" [disabled]="busy()">
        {{ registering() ? 'Create account' : 'Sign in' }}
      </button>
      <p class="switch">
        {{ registering() ? 'Already have an account?' : 'No account yet?' }}
        <button type="button" class="link" (click)="toggleMode()">
          {{ registering() ? 'Sign in' : 'Create one' }}
        </button>
      </p>
    </form>
  `,
  styles: `
    .login {
      max-width: 22rem;
      margin: 12vh auto 0;
      display: grid;
      gap: 1rem;
      padding: 1.5rem;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 12px;
    }
    h1 {
      margin: 0;
      font-size: 1.25rem;
    }
    label {
      display: grid;
      gap: 0.35rem;
      font-size: 0.875rem;
      color: var(--muted);
    }
    .switch {
      margin: 0;
      font-size: 0.875rem;
      color: var(--muted);
    }
  `,
})
export class Login {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  email = '';
  displayName = '';
  password = '';
  readonly registering = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');

  protected readonly defaultDisplayName = defaultDisplayName;

  toggleMode(): void {
    this.registering.update((registering) => !registering);
    this.error.set('');
  }

  submit(): void {
    this.busy.set(true);
    this.error.set('');
    const request = this.registering()
      ? this.api.register(
          this.email,
          this.displayName.trim() || defaultDisplayName(this.email),
          this.password,
        )
      : this.api.login(this.email, this.password);
    request.subscribe({
      next: (response: AuthResponse) => {
        this.auth.signIn(response.accessToken, response.user);
        this.router.navigate(['/']);
      },
      error: (error) => {
        this.error.set(errorMessage(error));
        this.busy.set(false);
      },
    });
  }
}
