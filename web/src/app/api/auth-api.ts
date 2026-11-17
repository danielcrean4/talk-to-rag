import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface User {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  user: User;
}

/** In single-user mode the API has no accounts and hands out a session without a sign-in. */
export interface SessionResponse {
  singleUser: boolean;
  session: AuthResponse | null;
}

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  session(): Observable<SessionResponse> {
    return this.http.get<SessionResponse>('/api/auth/session');
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', { email, password });
  }

  /** Answers 404 in single-user mode, where there are no accounts. */
  register(email: string, displayName: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', { email, displayName, password });
  }

  me(): Observable<User> {
    return this.http.get<User>('/api/auth/me');
  }
}
