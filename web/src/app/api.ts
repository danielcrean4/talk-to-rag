import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ProposalState = 'Proposed' | 'Accepted';

export interface Concept {
  id: string;
  name: string;
  summary: string | null;
  state: ProposalState;
  proposedByModel: boolean;
  passageCount: number;
}

/** "Understand prerequisiteId before conceptId." */
export interface Prerequisite {
  conceptId: string;
  prerequisiteId: string;
  state: ProposalState;
}

export interface ConceptGraph {
  concepts: Concept[];
  prerequisites: Prerequisite[];
}

export interface Place {
  sourceId: string;
  sourceTitle: string;
  pageNumber: number;
  pageLabel: string | null;
  section: string | null;
  snippet: string;
}

export interface ConceptPassage extends Place {
  id: string;
}

export interface PassageSuggestion extends Place {
  linked: boolean;
}

export interface ConceptDetails {
  concept: Concept;
  passages: ConceptPassage[];
}

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

export interface Tag {
  id: string;
  name: string;
  createdAtUtc: string;
}

export interface CreateSourceResponse {
  id: string;
  title: string;
  status: SourceStatus;
}

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

export interface ConversationSummary {
  id: string;
  title: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface MessageCitation {
  number: number;
  /** Null once the source has been ingested again and the chunk replaced. */
  sourceChunkId: string | null;
  sourceId: string;
  pageNumber: number;
  pageLabel: string | null;
  section: string | null;
  content: string;
}

export interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAtUtc: string;
  citations: MessageCitation[];
}

export interface ConversationDetails extends ConversationSummary {
  messages: Message[];
}

@Injectable({ providedIn: 'root' })
export class Api {
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

  retrySource(id: string): Observable<void> {
    return this.http.post<void>(`/api/sources/${id}/retry`, {});
  }

  deleteSource(id: string): Observable<void> {
    return this.http.delete<void>(`/api/sources/${id}`);
  }

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

  conversations(): Observable<ConversationSummary[]> {
    return this.http.get<ConversationSummary[]>('/api/conversations');
  }

  conversation(id: string): Observable<ConversationDetails> {
    return this.http.get<ConversationDetails>(`/api/conversations/${id}`);
  }

  transcribe(audio: Blob): Observable<{ text: string }> {
    const form = new FormData();
    form.append('audio', audio, 'question');
    return this.http.post<{ text: string }>('/api/speech/transcribe', form);
  }

  /** A WAV of the text spoken by the local voice model; 503 when no voice model is set up. */
  synthesize(text: string): Observable<Blob> {
    return this.http.post('/api/speech/synthesize', { text }, { responseType: 'blob' });
  }

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

  graph(): Observable<ConceptGraph> {
    return this.http.get<ConceptGraph>('/api/concepts/graph');
  }

  createConcept(name: string): Observable<Concept> {
    return this.http.post<Concept>('/api/concepts', { name });
  }

  updateConcept(id: string, changes: { name?: string; summary?: string }): Observable<Concept> {
    return this.http.patch<Concept>(`/api/concepts/${id}`, changes);
  }

  deleteConcept(id: string): Observable<void> {
    return this.http.delete<void>(`/api/concepts/${id}`);
  }

  accept(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/accept`, {});
  }

  reject(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/reject`, {});
  }

  addPrerequisite(id: string, prerequisiteId: string): Observable<ConceptGraph> {
    return this.http.put<ConceptGraph>(`/api/concepts/${id}/prerequisites/${prerequisiteId}`, {});
  }

  removePrerequisite(id: string, prerequisiteId: string): Observable<ConceptGraph> {
    return this.http.delete<ConceptGraph>(`/api/concepts/${id}/prerequisites/${prerequisiteId}`);
  }

  split(id: string, names: string[]): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/split`, { names });
  }

  proposePrerequisites(id: string): Observable<ConceptGraph> {
    return this.http.post<ConceptGraph>(`/api/concepts/${id}/propose-prerequisites`, {});
  }

  details(id: string): Observable<ConceptDetails> {
    return this.http.get<ConceptDetails>(`/api/concepts/${id}`);
  }

  passageSuggestions(id: string): Observable<PassageSuggestion[]> {
    return this.http.get<PassageSuggestion[]>(`/api/concepts/${id}/passage-suggestions`);
  }

  addPassage(id: string, place: Place): Observable<ConceptPassage> {
    const { sourceId, pageNumber, pageLabel, section, snippet } = place;
    return this.http.post<ConceptPassage>(`/api/concepts/${id}/passages`, {
      sourceId,
      pageNumber,
      pageLabel,
      section,
      snippet,
    });
  }

  removePassage(id: string, passageId: string): Observable<void> {
    return this.http.delete<void>(`/api/concepts/${id}/passages/${passageId}`);
  }
}
