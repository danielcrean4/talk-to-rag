import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface Tag {
  id: string;
  name: string;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class TagsApi {
  private readonly http = inject(HttpClient);

  tags(): Observable<Tag[]> {
    return this.http.get<Tag[]>('/api/tags');
  }

  createTag(name: string): Observable<Tag> {
    return this.http.post<Tag>('/api/tags', { name });
  }

  deleteTag(id: string): Observable<void> {
    return this.http.delete<void>(`/api/tags/${id}`);
  }

  tagSource(sourceId: string, tagId: string): Observable<void> {
    return this.http.put<void>(`/api/sources/${sourceId}/tags/${tagId}`, {});
  }

  untagSource(sourceId: string, tagId: string): Observable<void> {
    return this.http.delete<void>(`/api/sources/${sourceId}/tags/${tagId}`);
  }
}
