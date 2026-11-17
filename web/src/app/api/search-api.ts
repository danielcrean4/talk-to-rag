import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type SearchMode = 'hybrid' | 'semantic' | 'keyword';

export interface SearchRequest {
  query: string;
  notebookIds?: string[] | null;
  tagIds?: string[] | null;
  mode?: SearchMode;
  expand?: boolean;
  limit?: number;
  conversationId?: string | null;
  maxCosineDistance?: number;
}

export interface PassageHit {
  /** Rank across the whole search; the summary cites passages by this number. */
  number: number;
  chunkId: string;
  pageNumber: number;
  pageLabel: string | null;
  section: string | null;
  /** Text with the matched words wrapped in <mark>. */
  snippet: string;
  score: number;
  matchedBy: string[];
}

export interface SourceSummary {
  id: string;
  title: string;
  contentType: string;
}

export interface PassageGroup {
  source: SourceSummary;
  passages: PassageHit[];
}

export interface SearchResponse {
  conversationId: string;
  query: string;
  groups: PassageGroup[];
}

@Injectable({ providedIn: 'root' })
export class SearchApi {
  private readonly http = inject(HttpClient);

  search(request: SearchRequest): Observable<SearchResponse> {
    return this.http.post<SearchResponse>('/api/search', request);
  }

  /** The summary arrives as server-sent events; each progress event carries all the text so far. */
  summaryStream(conversationId: string): Observable<HttpEvent<string>> {
    return this.http.get(`/api/search/${conversationId}/summary/stream`, {
      observe: 'events',
      reportProgress: true,
      responseType: 'text',
    });
  }
}
