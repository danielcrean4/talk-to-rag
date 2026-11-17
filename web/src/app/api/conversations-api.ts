import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

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
export class ConversationsApi {
  private readonly http = inject(HttpClient);

  conversations(): Observable<ConversationSummary[]> {
    return this.http.get<ConversationSummary[]>('/api/conversations');
  }

  conversation(id: string): Observable<ConversationDetails> {
    return this.http.get<ConversationDetails>(`/api/conversations/${id}`);
  }
}
