import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { catchError, of, switchMap, tap } from 'rxjs';
import { Api } from './api';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { Auth, authInterceptor } from './auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Ask the API whether it has accounts before the first route decides to show the sign-in form.
    // With accounts, a token kept from an earlier visit is checked and tells whose it is.
    provideAppInitializer(() => {
      const auth = inject(Auth);
      const api = inject(Api);
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
        catchError(() => of(null)),
      );
    }),
  ],
};
