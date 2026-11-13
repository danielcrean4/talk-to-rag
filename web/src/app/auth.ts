import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { User } from './api';

const STORAGE_KEY = 'talk-to-rag.token';

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly router = inject(Router);
  readonly token = signal<string | null>(localStorage.getItem(STORAGE_KEY));
  readonly signedIn = computed(() => this.token() !== null);
  /** Who is signed in, shown next to the sign-out button when the API has accounts. */
  readonly user = signal<User | null>(null);

  /** Set at startup when the API runs without accounts. */
  singleUser = false;

  signIn(token: string, user: User | null = null): void {
    localStorage.setItem(STORAGE_KEY, token);
    this.token.set(token);
    this.user.set(user);
  }

  signOut(): void {
    if (this.singleUser) {
      // There is nobody to sign out; an expired session is replaced on reload.
      location.reload();
      return;
    }
    localStorage.removeItem(STORAGE_KEY);
    this.token.set(null);
    this.user.set(null);
    this.router.navigate(['/login']);
  }
}

export const signedInGuard: CanActivateFn = () =>
  inject(Auth).signedIn() || inject(Router).createUrlTree(['/login']);

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(Auth);
  const token = auth.token();
  const authorized = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;
  return next(authorized).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && token) {
        auth.signOut();
      }
      return throwError(() => error);
    }),
  );
};

/** The API answers errors as ProblemDetails; this picks the sentence meant for people. */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'The API is not reachable. Is the stack running (make up)?';
    }
    return error.error?.detail ?? error.error?.title ?? `Request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}
