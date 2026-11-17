import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface Notebook {
  id: string;
  name: string;
  description: string | null;
  sourceCount: number;
  createdAtUtc: string;
}

export interface NotebookDetails {
  id: string;
  name: string;
  description: string | null;
  sourceIds: string[];
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class NotebooksApi {
  private readonly http = inject(HttpClient);

  notebooks(): Observable<Notebook[]> {
    return this.http.get<Notebook[]>('/api/notebooks');
  }

  notebook(id: string): Observable<NotebookDetails> {
    return this.http.get<NotebookDetails>(`/api/notebooks/${id}`);
  }

  createNotebook(name: string, description: string | null): Observable<Notebook> {
    return this.http.post<Notebook>('/api/notebooks', { name, description });
  }

  addSourceToNotebook(notebookId: string, sourceId: string): Observable<void> {
    return this.http.put<void>(`/api/notebooks/${notebookId}/sources/${sourceId}`, {});
  }
}
