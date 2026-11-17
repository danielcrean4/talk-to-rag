import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, of, switchMap, tap } from 'rxjs';
import { AuthApi } from './api/auth-api';
import { Auth, errorMessage } from './auth';

/**
 * Asks the API whether it has accounts before the first route decides to show the sign-in form.
 * With accounts, a token kept from an earlier visit is checked and tells whose it is.
 * A refusal means signing in again; an API that can't be reached is reported, since a sign-in form couldn't work either.
 */
export function startSession(): Observable<unknown> {
  const auth = inject(Auth);
  const api = inject(AuthApi);
  // Kept before the first navigation, which may change the address, so a retry returns to it.
  const requested = location.href;
  auth.unreachable.set(null);
  return api.session().pipe(
    tap((response) => {
      auth.singleUser = response.singleUser;
      if (response.session) {
        auth.signIn(response.session.accessToken, response.session.user);
      }
    }),
    switchMap((response) =>
      !response.singleUser && auth.signedIn()
        ? api.me().pipe(tap((user) => auth.user.set(user)))
        : of(null),
    ),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
        auth.forget();
      } else {
        auth.unreachable.set({ reason: errorMessage(error), url: requested });
      }
      return of(null);
    }),
  );
}
