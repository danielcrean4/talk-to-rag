import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type SourceStatus = 'Pending' | 'Processing' | 'Ready' | 'Failed';

export interface Source {
  id: string;
  title: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status: SourceStatus;
  failureReason: string | null;
  chunkCount: number;
  createdAtUtc: string;
  tagIds: string[];
  license: string | null;
  /** Where the source came from; the API accepts only absolute http and https URLs. */
  sourceUrl: string | null;
}

/** A field left out keeps its value; an empty string clears it. */
export interface SourceDetailsChange {
  license?: string;
  sourceUrl?: string;
}

export interface CreateSourceResponse {
  id: string;
  title: string;
  status: SourceStatus;
}

/** Opens one source's file, without a sign-in header, for a few minutes. */
export interface SourceFileLinkResponse {
  url: string;
  expiresAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class SourcesApi {
  private readonly http = inject(HttpClient);

  sources(): Observable<Source[]> {
    return this.http.get<Source[]>('/api/sources');
  }

  uploadSource(file: File): Observable<CreateSourceResponse> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<CreateSourceResponse>('/api/sources', form);
  }

  updateSource(id: string, changes: SourceDetailsChange): Observable<Source> {
    return this.http.patch<Source>(`/api/sources/${id}`, changes);
  }

  sourceFileLink(id: string): Observable<SourceFileLinkResponse> {
    return this.http.post<SourceFileLinkResponse>(`/api/sources/${id}/file-link`, {});
  }

  retrySource(id: string): Observable<void> {
    return this.http.post<void>(`/api/sources/${id}/retry`, {});
  }

  deleteSource(id: string): Observable<void> {
    return this.http.delete<void>(`/api/sources/${id}`);
  }
}
